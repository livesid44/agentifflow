using AgentifFlow.Api.Data;
using AgentifFlow.Api.Models;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.EntityFrameworkCore;

namespace AgentifFlow.Api.Services;

/// <summary>
/// Background worker that polls Azure Blob Storage at a configurable interval,
/// detects new CSV files, validates them, and inserts valid data into SQL.
/// Sends email notifications on validation failures.
/// </summary>
public class BlobWatcherBackgroundService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<BlobWatcherBackgroundService> _logger;

    public BlobWatcherBackgroundService(
        IServiceScopeFactory scopeFactory,
        ILogger<BlobWatcherBackgroundService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("BlobWatcherBackgroundService started.");

        while (!stoppingToken.IsCancellationRequested)
        {
            int pollInterval = 60; // default

            try
            {
                using var scope = _scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AgentifFlowDbContext>();

                var config = await db.AppConfigurations.FirstOrDefaultAsync(stoppingToken);

                if (config is null || !config.AgentFlowEnabled)
                {
                    // Agent flow not yet configured or disabled — wait briefly and re-check
                    await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
                    continue;
                }

                pollInterval = config.BlobPollIntervalSeconds > 0
                    ? config.BlobPollIntervalSeconds
                    : 60;

                if (!string.IsNullOrWhiteSpace(config.BlobStorageConnectionString) &&
                    !string.IsNullOrWhiteSpace(config.BlobContainerName))
                {
                    await PollBlobStorageAsync(scope.ServiceProvider, config, stoppingToken);
                }
                else
                {
                    _logger.LogDebug("Blob storage not configured — skipping poll cycle.");
                }
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in BlobWatcherBackgroundService poll cycle.");
            }

            await Task.Delay(TimeSpan.FromSeconds(pollInterval), stoppingToken);
        }

        _logger.LogInformation("BlobWatcherBackgroundService stopped.");
    }

    private async Task PollBlobStorageAsync(
        IServiceProvider services,
        AppConfiguration config,
        CancellationToken ct)
    {
        _logger.LogInformation("Polling blob container '{Container}'", config.BlobContainerName);

        var blobServiceClient    = new BlobServiceClient(config.BlobStorageConnectionString);
        var containerClient      = blobServiceClient.GetBlobContainerClient(config.BlobContainerName);
        var jobService           = services.GetRequiredService<IBlobWatcherJobService>();
        var csvValidationService = services.GetRequiredService<ICsvValidationService>();
        var llmService           = services.GetRequiredService<ILlmService>();
        var mailService          = services.GetRequiredService<IGraphMailService>();

        await foreach (BlobItem blob in containerClient.GetBlobsAsync(cancellationToken: ct))
        {
            if (!blob.Name.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
                continue;

            // Skip already-tracked blobs (unless they're failed/rejected and eligible for retry)
            bool alreadyExists = await jobService.ExistsAsync(blob.Name, config.BlobContainerName);
            if (alreadyExists) continue;

            _logger.LogInformation("New CSV blob detected: {BlobName}", blob.Name);
            var job = await jobService.CreateAsync(blob.Name, config.BlobContainerName);

            await ProcessBlobAsync(
                job, blob.Name, containerClient, config,
                jobService, csvValidationService, llmService, mailService, ct);
        }
    }

    private async Task ProcessBlobAsync(
        BlobWatcherJob job,
        string blobName,
        BlobContainerClient container,
        AppConfiguration config,
        IBlobWatcherJobService jobService,
        ICsvValidationService csvValidation,
        ILlmService llmService,
        IGraphMailService mailService,
        CancellationToken ct)
    {
        // ── 1. Download CSV content ───────────────────────────────────────────
        string csvContent;
        try
        {
            var blobClient = container.GetBlobClient(blobName);
            var download   = await blobClient.DownloadContentAsync(ct);
            csvContent     = download.Value.Content.ToString();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to download blob {BlobName}", blobName);
            await jobService.UpdateStatusAsync(job.Id, BlobWatcherJobStatus.Failed,
                $"Download failed: {ex.Message}", "Blob download error.");
            return;
        }

        // ── 2. Validate CSV ───────────────────────────────────────────────────
        await jobService.UpdateStatusAsync(job.Id, BlobWatcherJobStatus.Validating,
            logEntry: "Starting CSV validation.");

        var validationResult = csvValidation.Validate(csvContent);

        if (!validationResult.IsValid)
        {
            var errorSummary = string.Join("; ", validationResult.Errors);
            _logger.LogWarning("CSV validation failed for {BlobName}: {Errors}", blobName, errorSummary);

            await jobService.UpdateStatusAsync(job.Id, BlobWatcherJobStatus.ValidationFailed,
                errorSummary, $"Validation failed: {errorSummary}");

            // ── 3a. Use LLM to draft a clear error email ──────────────────────
            string emailBody;
            try
            {
                emailBody = await llmService.SummarizeAsync(
                    $"CSV file '{blobName}' failed validation. Errors: {errorSummary}. " +
                    $"Row count attempted: {validationResult.RowCount}. " +
                    $"Please draft a professional email asking the user to review and fix the file.");
            }
            catch
            {
                emailBody = $"The CSV file '{blobName}' failed validation.\n\nErrors:\n{string.Join('\n', validationResult.Errors)}\n\nPlease review and re-upload the corrected file.";
            }

            // ── 3b. Send notification email ───────────────────────────────────
            if (!string.IsNullOrWhiteSpace(config.NotificationEmail))
            {
                try
                {
                    await mailService.SendEmailAsync(new SendEmailRequest
                    {
                        To      = config.NotificationEmail,
                        Subject = $"[AgentifFlow] CSV Validation Failed: {blobName}",
                        Body    = emailBody,
                        IsHtml  = false
                    });
                    await jobService.UpdateStatusAsync(job.Id, BlobWatcherJobStatus.AwaitingApproval,
                        logEntry: $"Notification email sent to {config.NotificationEmail}.");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to send notification email for {BlobName}", blobName);
                    await jobService.UpdateStatusAsync(job.Id, BlobWatcherJobStatus.ValidationFailed,
                        logEntry: $"Email notification failed: {ex.Message}");
                }
            }
            return;
        }

        // ── 4. Validation passed — insert into SQL ────────────────────────────
        await jobService.UpdateStatusAsync(job.Id, BlobWatcherJobStatus.Inserting,
            logEntry: $"Validation passed ({validationResult.RowCount} rows). Starting SQL insert.");

        try
        {
            int rowsInserted = await InsertCsvToSqlAsync(csvContent, blobName, config);
            await jobService.SetRowsInsertedAsync(job.Id, rowsInserted);
            await jobService.UpdateStatusAsync(job.Id, BlobWatcherJobStatus.Completed,
                logEntry: $"Successfully inserted {rowsInserted} rows into SQL.");
            _logger.LogInformation("Completed processing {BlobName}: {Rows} rows inserted.", blobName, rowsInserted);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SQL insert failed for {BlobName}", blobName);
            await jobService.UpdateStatusAsync(job.Id, BlobWatcherJobStatus.Failed,
                $"SQL insert failed: {ex.Message}",
                $"SQL insert error: {ex.Message}");

            if (job.RetryCount < config.MaxRetryCount)
                await jobService.IncrementRetryAsync(job.Id);
        }
    }

    /// <summary>
    /// Inserts the CSV rows into SQL using a bulk insert pattern.
    /// The table name is derived from the blob filename (without extension).
    /// Creates the table if it does not exist.
    /// </summary>
    private static async Task<int> InsertCsvToSqlAsync(
        string csvContent, string blobName, AppConfiguration config)
    {
        if (string.IsNullOrWhiteSpace(config.SqlConnectionString))
            throw new InvalidOperationException("SQL connection string is not configured.");

        var lines = csvContent
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(l => l.TrimEnd('\r'))
            .Where(l => !string.IsNullOrWhiteSpace(l))
            .ToArray();

        if (lines.Length < 2) return 0;

        var headers = SplitCsvLine(lines[0]);
        // Sanitise table name from blob filename
        var tableName = System.Text.RegularExpressions.Regex
            .Replace(System.IO.Path.GetFileNameWithoutExtension(blobName), @"[^A-Za-z0-9_]", "_");

        using var connection = new Microsoft.Data.SqlClient.SqlConnection(config.SqlConnectionString);
        await connection.OpenAsync();

        // Create table if not exists
        var createCols = string.Join(", ", headers.Select(h => $"[{h}] NVARCHAR(MAX)"));
        var createSql  = $"IF NOT EXISTS (SELECT * FROM sysobjects WHERE name='{tableName}' AND xtype='U') " +
                         $"CREATE TABLE [{tableName}] ({createCols})";
        using (var cmd = new Microsoft.Data.SqlClient.SqlCommand(createSql, connection))
            await cmd.ExecuteNonQueryAsync();

        int inserted = 0;
        for (int i = 1; i < lines.Length; i++)
        {
            var cols      = SplitCsvLine(lines[i]);
            var paramList = string.Join(", ", headers.Select((_, idx) => $"@p{idx}"));
            var colList   = string.Join(", ", headers.Select(h => $"[{h}]"));
            var insertSql = $"INSERT INTO [{tableName}] ({colList}) VALUES ({paramList})";

            using var cmd = new Microsoft.Data.SqlClient.SqlCommand(insertSql, connection);
            for (int j = 0; j < headers.Length; j++)
            {
                var val = j < cols.Length ? (object)cols[j] : DBNull.Value;
                cmd.Parameters.AddWithValue($"@p{j}", val);
            }
            await cmd.ExecuteNonQueryAsync();
            inserted++;
        }

        return inserted;
    }

    private static string[] SplitCsvLine(string line)
    {
        var fields  = new List<string>();
        var current = new System.Text.StringBuilder();
        bool inQuotes = false;

        foreach (char c in line)
        {
            if (c == '"')        { inQuotes = !inQuotes; }
            else if (c == ',' && !inQuotes) { fields.Add(current.ToString().Trim()); current.Clear(); }
            else                 { current.Append(c); }
        }
        fields.Add(current.ToString().Trim());
        return [.. fields];
    }
}
