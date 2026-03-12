using AgentifFlow.Api.Data;
using AgentifFlow.Api.Models;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.EntityFrameworkCore;

namespace AgentifFlow.Api.Services;

/// <summary>
/// Background worker that, when Agent Flow is enabled, polls Azure Blob Storage at a
/// configurable interval, detects new CSV files, validates them, and inserts valid data
/// into SQL.
///
/// <para>
/// <b>Email notifications</b> are sent to the configured <c>NotificationEmail</c> in two cases:
/// <list type="bullet">
///   <item>No CSV files are found in the container (once per agent run-session, reset when a file is processed).</item>
///   <item>A CSV file fails column/data validation.</item>
/// </list>
/// </para>
/// </summary>
public class BlobWatcherBackgroundService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<BlobWatcherBackgroundService> _logger;

    // Track whether the "no files found" notification has already been sent this run-session
    // so we do not flood the mailbox on every poll cycle.  Resets when a file is successfully
    // detected, giving the operator a fresh alert after the container is drained and refilled.
    private bool _noFileNotificationSent;

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
                    await PollBlobStorageAsync(scope.ServiceProvider, config, db, stoppingToken);
                }
                else
                {
                    _logger.LogWarning("Blob storage not fully configured — skipping poll cycle.");

                    // Notify operator that blob storage is not configured
                    if (!string.IsNullOrWhiteSpace(config.NotificationEmail) &&
                        !_noFileNotificationSent)
                    {
                        await TrySendEmailAsync(
                            scope.ServiceProvider.GetRequiredService<IGraphMailService>(),
                            config.NotificationEmail,
                            "[AgentifFlow] Blob Storage Not Configured",
                            "The AgentifFlow agent is enabled but Blob Storage has not been configured.\n\n" +
                            "Please go to Integration Settings and enter a valid Storage Account " +
                            "Connection String and Container Name, then save the configuration.",
                            "Blob storage not configured notification");
                        _noFileNotificationSent = true;
                    }
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
        AgentifFlowDbContext db,
        CancellationToken ct)
    {
        _logger.LogInformation("Polling blob container '{Container}'", config.BlobContainerName);

        var blobServiceClient    = new BlobServiceClient(config.BlobStorageConnectionString);
        var containerClient      = blobServiceClient.GetBlobContainerClient(config.BlobContainerName);
        var jobService           = services.GetRequiredService<IBlobWatcherJobService>();
        var csvValidationService = services.GetRequiredService<ICsvValidationService>();
        var llmService           = services.GetRequiredService<ILlmService>();
        var mailService          = services.GetRequiredService<IGraphMailService>();

        // ── 1. Detect and process new blobs ──────────────────────────────────
        int csvFilesDetected = 0;

        await foreach (BlobItem blob in containerClient.GetBlobsAsync(cancellationToken: ct))
        {
            if (!blob.Name.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
                continue;

            csvFilesDetected++;

            // Skip already-tracked blobs that are not pending a retry
            bool alreadyExists = await jobService.ExistsAsync(blob.Name, config.BlobContainerName);
            if (alreadyExists) continue;

            _logger.LogInformation("New CSV blob detected: {BlobName}", blob.Name);

            // A new file has been found — reset the "no file" notification flag so the
            // operator gets a fresh alert if the container becomes empty again later.
            _noFileNotificationSent = false;

            var job = await jobService.CreateAsync(blob.Name, config.BlobContainerName);

            await ProcessBlobAsync(
                job, blob.Name, containerClient, config,
                jobService, csvValidationService, llmService, mailService, ct);
        }

        // ── 2. Re-process blobs that were approved for retry ─────────────────
        var retryJobs = await db.BlobWatcherJobs
            .Where(j => j.Status == BlobWatcherJobStatus.Retrying)
            .ToListAsync(ct);

        foreach (var retryJob in retryJobs)
        {
            _logger.LogInformation("Re-processing retry job {JobId} for blob '{Blob}'",
                retryJob.Id, retryJob.BlobName);

            _noFileNotificationSent = false; // treat a retry as "active work"

            var retryContainer = string.IsNullOrWhiteSpace(retryJob.ContainerName)
                ? containerClient
                : blobServiceClient.GetBlobContainerClient(retryJob.ContainerName);

            await ProcessBlobAsync(
                retryJob, retryJob.BlobName, retryContainer, config,
                jobService, csvValidationService, llmService, mailService, ct);
        }

        // ── 3. No-file notification ───────────────────────────────────────────
        // Send ONE notification if no CSV files were found in the container and
        // retryJobs is also empty, so the operator knows to upload a file.
        if (csvFilesDetected == 0 && retryJobs.Count == 0 &&
            !_noFileNotificationSent &&
            !string.IsNullOrWhiteSpace(config.NotificationEmail))
        {
            _logger.LogInformation(
                "No CSV files found in container '{Container}' — sending notification email.",
                config.BlobContainerName);

            await TrySendEmailAsync(
                mailService,
                config.NotificationEmail,
                $"[AgentifFlow] No CSV Files Found in Container '{config.BlobContainerName}'",
                $"The AgentifFlow agent polled the blob container \"{config.BlobContainerName}\" " +
                $"but did not find any CSV files to process.\n\n" +
                $"Please upload a CSV file to the container, or verify that the container name " +
                $"and Storage Account Connection String are correct in the Integration Settings.\n\n" +
                $"The agent will continue polling every {config.BlobPollIntervalSeconds} seconds " +
                $"and will process files as soon as they appear.",
                $"No-file notification for container '{config.BlobContainerName}'");

            _noFileNotificationSent = true;
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

            await TrySendEmailAsync(
                mailService,
                config.NotificationEmail,
                $"[AgentifFlow] Failed to Download File: {blobName}",
                $"The AgentifFlow agent could not download the file \"{blobName}\" " +
                $"from container \"{config.BlobContainerName}\".\n\n" +
                $"Error: {ex.Message}\n\n" +
                "Please verify the file exists, the connection string is correct, and that the " +
                "storage account is accessible.",
                $"Download failure notification for '{blobName}'");
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

            // ── 3a. Draft a clear error email using LLM if available ──────────
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
                emailBody =
                    $"The CSV file \"{blobName}\" failed validation and could not be uploaded to the database.\n\n" +
                    $"Validation errors found:\n{string.Join('\n', validationResult.Errors.Select(e => "  • " + e))}\n\n" +
                    $"Please correct the file and re-upload it to the blob container \"{config.BlobContainerName}\".";
            }

            // ── 3b. Send notification email ───────────────────────────────────
            var sent = await TrySendEmailAsync(
                mailService,
                config.NotificationEmail,
                $"[AgentifFlow] CSV Validation Failed: {blobName}",
                emailBody,
                $"Validation-failure notification for '{blobName}'");

            var nextStatus = sent
                ? BlobWatcherJobStatus.AwaitingApproval
                : BlobWatcherJobStatus.ValidationFailed;

            await jobService.UpdateStatusAsync(job.Id, nextStatus,
                logEntry: sent
                    ? $"Notification email sent to {config.NotificationEmail}."
                    : "Notification email skipped (NotificationEmail not configured).");
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

            await TrySendEmailAsync(
                mailService,
                config.NotificationEmail,
                $"[AgentifFlow] SQL Insert Failed: {blobName}",
                $"The CSV file \"{blobName}\" passed validation but could not be inserted into the database.\n\n" +
                $"Error: {ex.Message}\n\n" +
                "Please check the SQL connection string and ensure the database is reachable.",
                $"SQL-insert failure notification for '{blobName}'");

            if (job.RetryCount < config.MaxRetryCount)
                await jobService.IncrementRetryAsync(job.Id);
        }
    }

    /// <summary>
    /// Sends a notification email, swallowing any exception so a mail failure never
    /// crashes the processing pipeline.  Returns <c>true</c> on success.
    /// </summary>
    private async Task<bool> TrySendEmailAsync(
        IGraphMailService mailService,
        string? to,
        string subject,
        string body,
        string logContext)
    {
        if (string.IsNullOrWhiteSpace(to))
        {
            _logger.LogDebug("Skipping email '{Subject}' — NotificationEmail is not configured.", subject);
            return false;
        }

        try
        {
            await mailService.SendEmailAsync(new SendEmailRequest
            {
                To      = to,
                Subject = subject,
                Body    = body,
                IsHtml  = false
            });
            _logger.LogInformation("Email sent ({Context}) to {To}", logContext, to);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send email ({Context}) to {To}", logContext, to);
            return false;
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
