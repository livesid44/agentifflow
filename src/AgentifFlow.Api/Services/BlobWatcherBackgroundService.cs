using AgentifFlow.Api.Data;
using AgentifFlow.Api.Models;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.EntityFrameworkCore;

namespace AgentifFlow.Api.Services;

/// <summary>
/// Background worker that, when Agent Flow is enabled:
/// <list type="number">
///   <item>Polls Azure Blob Storage for new CSV files, validates them, and inserts into SQL.</item>
///   <item>Sends a notification email with a unique <c>[Ref: AGNT-…]</c> token in the subject
///         whenever a file is missing or fails validation.</item>
///   <item>Each poll cycle scans the inbox for unread replies whose subject contains one of
///         those tokens and records the reply against the originating job.</item>
/// </list>
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
            int pollInterval = 60;

            try
            {
                using var scope = _scopeFactory.CreateScope();
                var db     = scope.ServiceProvider.GetRequiredService<AgentifFlowDbContext>();
                var config = await db.AppConfigurations.FirstOrDefaultAsync(stoppingToken);

                if (config is null || !config.AgentFlowEnabled)
                {
                    await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
                    continue;
                }

                pollInterval = config.BlobPollIntervalSeconds > 0 ? config.BlobPollIntervalSeconds : 60;

                // ── 1. Blob scan ──────────────────────────────────────────────
                if (!string.IsNullOrWhiteSpace(config.BlobStorageConnectionString) &&
                    !string.IsNullOrWhiteSpace(config.BlobContainerName))
                {
                    await PollBlobStorageAsync(scope.ServiceProvider, config, db, stoppingToken);
                }
                else
                {
                    _logger.LogWarning("Blob storage not fully configured — skipping blob poll.");
                    await SendBlobNotConfiguredNotificationAsync(
                        scope.ServiceProvider, config, db, stoppingToken);
                }

                // ── 2. Inbox reply scan ───────────────────────────────────────
                await PollInboxForRepliesAsync(scope.ServiceProvider, db, stoppingToken);
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

    // ──────────────────────────────────────────────────────────────────────────
    // Blob storage polling
    // ──────────────────────────────────────────────────────────────────────────

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

        int csvFilesDetected = 0;

        await foreach (BlobItem blob in containerClient.GetBlobsAsync(cancellationToken: ct))
        {
            if (!blob.Name.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
                continue;

            csvFilesDetected++;

            bool alreadyExists = await jobService.ExistsAsync(blob.Name, config.BlobContainerName);
            if (alreadyExists) continue;

            _logger.LogInformation("New CSV blob detected: {BlobName}", blob.Name);
            var job = await jobService.CreateAsync(blob.Name, config.BlobContainerName);
            await ProcessBlobAsync(job, blob.Name, containerClient, config,
                jobService, csvValidationService, llmService, mailService, ct);
        }

        // Re-process blobs approved for retry
        var retryJobs = await db.BlobWatcherJobs
            .Where(j => j.Status == BlobWatcherJobStatus.Retrying)
            .ToListAsync(ct);

        foreach (var retryJob in retryJobs)
        {
            _logger.LogInformation("Re-processing retry job {JobId} for blob '{Blob}'",
                retryJob.Id, retryJob.BlobName);

            var retryContainer = string.IsNullOrWhiteSpace(retryJob.ContainerName)
                ? containerClient
                : blobServiceClient.GetBlobContainerClient(retryJob.ContainerName);

            await ProcessBlobAsync(retryJob, retryJob.BlobName, retryContainer, config,
                jobService, csvValidationService, llmService, mailService, ct);
        }

        // Send a "no CSV files found" alert if nothing was detected this cycle
        if (csvFilesDetected == 0 && retryJobs.Count == 0)
            await SendNoFileNotificationAsync(services, config, db, ct);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // "No file found" notification  —  creates a tracked job so the ref
    // persists across service restarts and can be matched with email replies
    // ──────────────────────────────────────────────────────────────────────────

    private const string NoFileBlobName            = "[container-scan]";
    private const string BlobNotConfiguredBlobName = "[blob-not-configured]";

    private async Task SendNoFileNotificationAsync(
        IServiceProvider services,
        AppConfiguration config,
        AgentifFlowDbContext db,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(config.NotificationEmail)) return;

        // Only send ONE notification per empty-container event (idempotent)
        bool alreadySent = await db.BlobWatcherJobs.AnyAsync(j =>
            j.BlobName      == NoFileBlobName &&
            j.ContainerName == config.BlobContainerName &&
            (j.Status == BlobWatcherJobStatus.AwaitingApproval ||
             j.Status == BlobWatcherJobStatus.ReplyReceived), ct);

        if (alreadySent) return;

        var jobService  = services.GetRequiredService<IBlobWatcherJobService>();
        var mailService = services.GetRequiredService<IGraphMailService>();

        var job             = await jobService.CreateAsync(NoFileBlobName, config.BlobContainerName);
        var notificationRef = GenerateRef();

        _logger.LogInformation(
            "No CSV files in container '{Container}' — sending notification [{Ref}].",
            config.BlobContainerName, notificationRef);

        var subject = $"[AgentifFlow] No CSV Files Found in '{config.BlobContainerName}' [Ref: {notificationRef}]";
        var body    =
            $"The AgentifFlow agent polled the blob container \"{config.BlobContainerName}\" " +
            $"but did not find any CSV files to process.\n\n" +
            "Please upload a CSV file to the container, or verify the container name and " +
            "Storage Account Connection String in the Integration Settings.\n\n" +
            $"The agent will continue polling every {config.BlobPollIntervalSeconds} seconds " +
            "and will process any file as soon as it appears.\n\n" +
            "Reply to this email on this thread when the file is ready — " +
            "the agent monitors this reference and will update the job record automatically.\n\n" +
            $"Reference: {notificationRef}";

        bool sent = await TrySendEmailAsync(mailService, config.NotificationEmail,
            subject, body, $"no-file [{notificationRef}]");

        await jobService.SetNotificationRefAsync(job.Id, notificationRef);
        await jobService.UpdateStatusAsync(job.Id,
            sent ? BlobWatcherJobStatus.AwaitingApproval : BlobWatcherJobStatus.Failed,
            logEntry: sent
                ? $"No-file notification sent to {config.NotificationEmail} [Ref: {notificationRef}]."
                : "No-file notification could not be sent — Graph API may not be configured.");
    }

    private async Task SendBlobNotConfiguredNotificationAsync(
        IServiceProvider services,
        AppConfiguration config,
        AgentifFlowDbContext db,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(config.NotificationEmail)) return;

        bool alreadySent = await db.BlobWatcherJobs.AnyAsync(j =>
            j.BlobName == BlobNotConfiguredBlobName &&
            (j.Status == BlobWatcherJobStatus.AwaitingApproval ||
             j.Status == BlobWatcherJobStatus.ReplyReceived), ct);

        if (alreadySent) return;

        var jobService  = services.GetRequiredService<IBlobWatcherJobService>();
        var mailService = services.GetRequiredService<IGraphMailService>();

        var job             = await jobService.CreateAsync(BlobNotConfiguredBlobName, null);
        var notificationRef = GenerateRef();

        var subject = $"[AgentifFlow] Blob Storage Not Configured [Ref: {notificationRef}]";
        var body    =
            "The AgentifFlow agent is enabled but Blob Storage has not been fully configured.\n\n" +
            "Please go to Integration Settings and enter a valid Storage Account Connection String " +
            "and Container Name, then save the configuration.\n\n" +
            "Reply to this email once the settings have been updated.\n\n" +
            $"Reference: {notificationRef}";

        bool sent = await TrySendEmailAsync(mailService, config.NotificationEmail,
            subject, body, $"blob-not-configured [{notificationRef}]");

        await jobService.SetNotificationRefAsync(job.Id, notificationRef);
        await jobService.UpdateStatusAsync(job.Id,
            sent ? BlobWatcherJobStatus.AwaitingApproval : BlobWatcherJobStatus.Failed,
            logEntry: sent
                ? $"Blob-not-configured notification sent [Ref: {notificationRef}]."
                : "Notification could not be sent — Graph API may not be configured.");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Inbox reply polling
    // Each poll cycle: fetch recent unread messages, match subject against known
    // notification refs, record reply, and mark the message as read.
    // ──────────────────────────────────────────────────────────────────────────

    private async Task PollInboxForRepliesAsync(
        IServiceProvider services,
        AgentifFlowDbContext db,
        CancellationToken ct)
    {
        // Only proceed when there are jobs actively waiting for a reply
        var waitingJobs = await db.BlobWatcherJobs
            .Where(j => j.NotificationRef != null &&
                        (j.Status == BlobWatcherJobStatus.AwaitingApproval ||
                         j.Status == BlobWatcherJobStatus.ValidationFailed))
            .ToListAsync(ct);

        if (waitingJobs.Count == 0) return;

        var mailService = services.GetRequiredService<IGraphMailService>();
        var jobService  = services.GetRequiredService<IBlobWatcherJobService>();

        List<EmailMessage> inboxMessages;
        try
        {
            // Fetch the 50 most-recent messages; filter to unread only (read messages were
            // already processed in a previous cycle)
            inboxMessages = (await mailService.GetInboxMessagesAsync(top: 50))
                .Where(m => !m.IsRead)
                .ToList();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not fetch inbox for reply polling (Graph API may be unavailable).");
            return;
        }

        if (inboxMessages.Count == 0) return;

        foreach (var job in waitingJobs)
        {
            // Replies from any mail client will include the original subject (with "Re:" prefix
            // and our "[Ref: AGNT-…]" token still present)
            var reply = inboxMessages.FirstOrDefault(m =>
                m.Subject.Contains(job.NotificationRef!, StringComparison.OrdinalIgnoreCase));

            if (reply is null) continue;

            _logger.LogInformation(
                "Reply received for job {JobId} [Ref: {Ref}] from '{From}'",
                job.Id, job.NotificationRef, reply.From);

            await jobService.SetUserReplyAsync(job.Id, reply.From, reply.BodyPreview);

            // Mark the inbox copy as read so the next cycle does not process it again
            try   { await mailService.MarkAsReadAsync(reply.Id); }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not mark reply message {MsgId} as read.", reply.Id);
            }
        }
    }

    // ──────────────────────────────────────────────────────────────────────────
    // CSV processing pipeline
    // ──────────────────────────────────────────────────────────────────────────

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
        // ── 1. Download ───────────────────────────────────────────────────────
        string csvContent;
        try
        {
            var download = await container.GetBlobClient(blobName).DownloadContentAsync(ct);
            csvContent   = download.Value.Content.ToString();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to download blob {BlobName}", blobName);
            await jobService.UpdateStatusAsync(job.Id, BlobWatcherJobStatus.Failed,
                $"Download failed: {ex.Message}", "Blob download error.");

            var dlRef = GenerateRef();
            await jobService.SetNotificationRefAsync(job.Id, dlRef);
            await TrySendEmailAsync(mailService, config.NotificationEmail,
                $"[AgentifFlow] Failed to Download File: {blobName} [Ref: {dlRef}]",
                $"The AgentifFlow agent could not download \"{blobName}\" " +
                $"from container \"{config.BlobContainerName}\".\n\n" +
                $"Error: {ex.Message}\n\n" +
                "Please verify the file exists and the storage account is accessible.\n\n" +
                $"Reply to this email on this thread once the issue is resolved.\n\nReference: {dlRef}",
                $"download-failure [{dlRef}]");
            return;
        }

        // ── 2. Validate ───────────────────────────────────────────────────────
        await jobService.UpdateStatusAsync(job.Id, BlobWatcherJobStatus.Validating,
            logEntry: "Starting CSV validation.");

        var validationResult = csvValidation.Validate(csvContent);

        if (!validationResult.IsValid)
        {
            var errorSummary = string.Join("; ", validationResult.Errors);
            _logger.LogWarning("CSV validation failed for {BlobName}: {Errors}", blobName, errorSummary);

            await jobService.UpdateStatusAsync(job.Id, BlobWatcherJobStatus.ValidationFailed,
                errorSummary, $"Validation failed: {errorSummary}");

            // Draft human-readable error email (LLM when available; plain text fallback)
            string emailBody;
            try
            {
                emailBody = await llmService.SummarizeAsync(
                    $"CSV file '{blobName}' failed validation. Errors: {errorSummary}. " +
                    $"Row count attempted: {validationResult.RowCount}. " +
                    "Please draft a professional email asking the user to review and fix the file.");
            }
            catch
            {
                emailBody =
                    $"The CSV file \"{blobName}\" failed validation and could not be uploaded " +
                    $"to the database.\n\n" +
                    $"Validation errors:\n" +
                    string.Join('\n', validationResult.Errors.Select(e => "  \u2022 " + e));
            }

            var valRef = GenerateRef();
            emailBody +=
                $"\n\nPlease correct the file and re-upload it to container \"{config.BlobContainerName}\".\n\n" +
                "Reply to this email on this thread once the corrected file has been uploaded — " +
                "the agent monitors this reference and will automatically re-process the file.\n\n" +
                $"Reference: {valRef}";

            bool sent = await TrySendEmailAsync(mailService, config.NotificationEmail,
                $"[AgentifFlow] CSV Validation Failed: {blobName} [Ref: {valRef}]",
                emailBody,
                $"validation-failure [{valRef}]");

            await jobService.SetNotificationRefAsync(job.Id, valRef);
            await jobService.UpdateStatusAsync(job.Id,
                sent ? BlobWatcherJobStatus.AwaitingApproval : BlobWatcherJobStatus.ValidationFailed,
                logEntry: sent
                    ? $"Notification sent to {config.NotificationEmail} [Ref: {valRef}]. Awaiting reply."
                    : "Notification skipped (NotificationEmail not configured).");
            return;
        }

        // ── 3. Insert into SQL ────────────────────────────────────────────────
        await jobService.UpdateStatusAsync(job.Id, BlobWatcherJobStatus.Inserting,
            logEntry: $"Validation passed ({validationResult.RowCount} rows). Starting SQL insert.");

        try
        {
            int rowsInserted = await InsertCsvToSqlAsync(csvContent, blobName, config);
            await jobService.SetRowsInsertedAsync(job.Id, rowsInserted);
            await jobService.UpdateStatusAsync(job.Id, BlobWatcherJobStatus.Completed,
                logEntry: $"Successfully inserted {rowsInserted} rows into SQL.");
            _logger.LogInformation("Completed {BlobName}: {Rows} rows inserted.", blobName, rowsInserted);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SQL insert failed for {BlobName}", blobName);
            await jobService.UpdateStatusAsync(job.Id, BlobWatcherJobStatus.Failed,
                $"SQL insert failed: {ex.Message}", $"SQL insert error: {ex.Message}");

            var sqlRef = GenerateRef();
            await jobService.SetNotificationRefAsync(job.Id, sqlRef);
            await TrySendEmailAsync(mailService, config.NotificationEmail,
                $"[AgentifFlow] SQL Insert Failed: {blobName} [Ref: {sqlRef}]",
                $"The CSV file \"{blobName}\" passed validation but could not be inserted into " +
                $"the database.\n\nError: {ex.Message}\n\n" +
                "Please check the SQL connection string and ensure the database is reachable.\n\n" +
                $"Reply to this email on this thread once the issue is resolved.\n\nReference: {sqlRef}",
                $"sql-failure [{sqlRef}]");

            if (job.RetryCount < config.MaxRetryCount)
                await jobService.IncrementRetryAsync(job.Id);
        }
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Helpers
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Generates a short, globally-unique reference token that is embedded in every
    /// outbound notification subject line (e.g. "AGNT-A3B2C4D5").
    /// </summary>
    private static string GenerateRef() =>
        "AGNT-" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();

    /// <summary>
    /// Attempts to send a notification email, swallowing any exception so that a mail
    /// delivery failure never crashes the processing pipeline.
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
            _logger.LogDebug("Skipping '{Subject}' — NotificationEmail is not configured.", subject);
            return false;
        }

        try
        {
            await mailService.SendEmailAsync(new SendEmailRequest
            {
                To = to, Subject = subject, Body = body, IsHtml = false
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

    // ──────────────────────────────────────────────────────────────────────────
    // SQL bulk-insert
    // ──────────────────────────────────────────────────────────────────────────

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

        var headers   = SplitCsvLine(lines[0]);
        var tableName = System.Text.RegularExpressions.Regex
            .Replace(System.IO.Path.GetFileNameWithoutExtension(blobName), @"[^A-Za-z0-9_]", "_");

        using var connection = new Microsoft.Data.SqlClient.SqlConnection(config.SqlConnectionString);
        await connection.OpenAsync();

        var createCols = string.Join(", ", headers.Select(h => $"[{h}] NVARCHAR(MAX)"));
        var createSql  =
            $"IF NOT EXISTS (SELECT * FROM sysobjects WHERE name='{tableName}' AND xtype='U') " +
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
                cmd.Parameters.AddWithValue($"@p{j}", j < cols.Length ? (object)cols[j] : DBNull.Value);
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
            if (c == '"')                   { inQuotes = !inQuotes; }
            else if (c == ',' && !inQuotes) { fields.Add(current.ToString().Trim()); current.Clear(); }
            else                            { current.Append(c); }
        }
        fields.Add(current.ToString().Trim());
        return [.. fields];
    }
}
