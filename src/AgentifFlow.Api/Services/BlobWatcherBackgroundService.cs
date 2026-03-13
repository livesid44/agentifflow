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
            // Record when this cycle starts so we can honour the exact interval
            // even if processing takes some time.
            var cycleStart = DateTime.UtcNow;
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
                        scope.ServiceProvider, config, stoppingToken);
                }

                // ── 2. Inbox reply scan ───────────────────────────────────────
                await PollInboxForRepliesAsync(scope.ServiceProvider, config, db, stoppingToken);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in BlobWatcherBackgroundService poll cycle.");
            }

            // Wait only the time remaining in the interval so the period stays accurate
            // regardless of how long processing took.
            var elapsed  = DateTime.UtcNow - cycleStart;
            var waitTime = TimeSpan.FromSeconds(pollInterval) - elapsed;
            if (waitTime > TimeSpan.Zero)
                await Task.Delay(waitTime, stoppingToken);
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

        // ── Agent Designer file-name filter ───────────────────────────────────
        // When a BlobInputFilePattern is configured we only pick up blobs whose
        // file name (without extension) matches the expected name.  When the
        // "append date" flag is set, the expected name also contains today's date
        // in yyyyMMdd format.
        string? expectedBlobName = null;
        if (!string.IsNullOrWhiteSpace(config.BlobInputFilePattern))
        {
            var baseName = config.BlobInputFilePattern.TrimEnd('_');
            expectedBlobName = config.BlobInputAppendDate
                ? $"{baseName}_{DateTime.UtcNow:yyyyMMdd}.csv"
                : $"{baseName}.csv";
        }

        int csvFilesDetected = 0;

        await foreach (BlobItem blob in containerClient.GetBlobsAsync(cancellationToken: ct))
        {
            if (!blob.Name.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
                continue;

            // Skip blobs that don't match the configured file-name pattern.
            if (expectedBlobName is not null &&
                !string.Equals(blob.Name, expectedBlobName, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogDebug(
                    "Skipping blob '{BlobName}' — does not match expected pattern '{Expected}'.",
                    blob.Name, expectedBlobName);
                continue;
            }

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
        {
            if (config.NotifyOnFileNotFound)
                await SendNoFileNotificationAsync(services, config, ct);
            else
                _logger.LogDebug("No matching blobs found this cycle; NotifyOnFileNotFound is disabled.");
        }
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
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(config.NotificationEmail)) return;

        var jobService  = services.GetRequiredService<IBlobWatcherJobService>();
        var mailService = services.GetRequiredService<IGraphMailService>();

        // Create a fresh job record for this poll cycle so every "no file found"
        // event is visible in the dashboard log — regardless of what happened in
        // previous cycles.
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
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(config.NotificationEmail)) return;

        var jobService  = services.GetRequiredService<IBlobWatcherJobService>();
        var mailService = services.GetRequiredService<IGraphMailService>();

        // Create a fresh job record every cycle so every "blob not configured"
        // event is visible in the dashboard log.
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
    // When a reply is for a "no file found" or "validation failed" job, trigger
    // a fresh blob scan / re-process so the agent resumes automatically.
    // ──────────────────────────────────────────────────────────────────────────

    private async Task PollInboxForRepliesAsync(
        IServiceProvider services,
        AppConfiguration config,
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

            // ── Re-trigger processing based on job type ─────────────────────
            if (!string.IsNullOrWhiteSpace(config.BlobStorageConnectionString) &&
                !string.IsNullOrWhiteSpace(config.BlobContainerName))
            {
                if (job.BlobName == NoFileBlobName || job.BlobName == BlobNotConfiguredBlobName)
                {
                    // For sentinel "no-file" jobs the reply means the user has uploaded
                    // the file — kick off a fresh blob scan immediately.
                    _logger.LogInformation(
                        "Reply for sentinel job {JobId} — triggering immediate blob re-scan.", job.Id);
                    await PollBlobStorageAsync(services, config, db, ct);
                }
                else
                {
                    // For real-file jobs (validation failure, SQL failure …) the user has
                    // (presumably) replaced the file in blob storage — re-process the same blob.
                    _logger.LogInformation(
                        "Reply for file job {JobId} ('{Blob}') — scheduling retry.", job.Id, job.BlobName);
                    await jobService.IncrementRetryAsync(job.Id);
                }
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

            if (config.NotifyOnDataIssue)
            {
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
            }
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

            bool sent = false;
            if (config.NotifyOnDataIssue)
            {
                sent = await TrySendEmailAsync(mailService, config.NotificationEmail,
                    $"[AgentifFlow] CSV Validation Failed: {blobName} [Ref: {valRef}]",
                    emailBody,
                    $"validation-failure [{valRef}]");
            }

            await jobService.SetNotificationRefAsync(job.Id, valRef);
            await jobService.UpdateStatusAsync(job.Id,
                sent ? BlobWatcherJobStatus.AwaitingApproval : BlobWatcherJobStatus.ValidationFailed,
                logEntry: sent
                    ? $"Notification sent to {config.NotificationEmail} [Ref: {valRef}]. Awaiting reply."
                    : "Notification skipped (NotifyOnDataIssue disabled or NotificationEmail not configured).");
            return;
        }

        // ── 3. Insert into SQL ────────────────────────────────────────────────
        // Only perform SQL push when SqlPushEnabled is true (Agent Designer).
        if (config.SqlPushEnabled && !string.IsNullOrWhiteSpace(config.SqlConnectionString))
        {
            await jobService.UpdateStatusAsync(job.Id, BlobWatcherJobStatus.Inserting,
                logEntry: $"Validation passed ({validationResult.RowCount} rows). Starting SQL insert.");

            try
            {
                int rowsInserted = await InsertCsvToSqlAsync(csvContent, blobName, config);
                await jobService.SetRowsInsertedAsync(job.Id, rowsInserted);
                _logger.LogInformation("Completed {BlobName}: {Rows} rows inserted.", blobName, rowsInserted);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "SQL insert failed for {BlobName}", blobName);
                await jobService.UpdateStatusAsync(job.Id, BlobWatcherJobStatus.Failed,
                    $"SQL insert failed: {ex.Message}", $"SQL insert error: {ex.Message}");

                if (config.NotifyOnDataIssue)
                {
                    var sqlRef = GenerateRef();
                    await jobService.SetNotificationRefAsync(job.Id, sqlRef);
                    await TrySendEmailAsync(mailService, config.NotificationEmail,
                        $"[AgentifFlow] SQL Insert Failed: {blobName} [Ref: {sqlRef}]",
                        $"The CSV file \"{blobName}\" passed validation but could not be inserted into " +
                        $"the database.\n\nError: {ex.Message}\n\n" +
                        "Please check the SQL connection string and ensure the database is reachable.\n\n" +
                        $"Reply to this email on this thread once the issue is resolved.\n\nReference: {sqlRef}",
                        $"sql-failure [{sqlRef}]");
                }

                if (job.RetryCount < config.MaxRetryCount)
                    await jobService.IncrementRetryAsync(job.Id);
                return;
            }
        }
        else
        {
            await jobService.UpdateStatusAsync(job.Id, BlobWatcherJobStatus.Inserting,
                logEntry: config.SqlPushEnabled
                    ? $"Validation passed ({validationResult.RowCount} rows). SQL push skipped — connection string not configured."
                    : $"Validation passed ({validationResult.RowCount} rows). SQL push is disabled in Agent Designer.");
        }

        // ── 4. Archive rename ─────────────────────────────────────────────────
        // After a successful upload, rename the blob to the configured archive
        // pattern (e.g. "archive_test_20260312.csv") if one is defined.
        if (!string.IsNullOrWhiteSpace(config.BlobArchiveFilePattern))
        {
            var archiveBase = config.BlobArchiveFilePattern.TrimEnd('_');
            var archiveName = config.BlobArchiveAppendDate
                ? $"{archiveBase}_{DateTime.UtcNow:yyyyMMdd}.csv"
                : $"{archiveBase}.csv";

            try
            {
                var sourceBlob = container.GetBlobClient(blobName);
                var destBlob   = container.GetBlobClient(archiveName);

                // Copy → delete (Azure Blob Storage does not have a rename primitive)
                await destBlob.StartCopyFromUriAsync(sourceBlob.Uri, cancellationToken: ct);
                await sourceBlob.DeleteIfExistsAsync(cancellationToken: ct);

                await jobService.UpdateStatusAsync(job.Id, BlobWatcherJobStatus.Completed,
                    logEntry: $"File renamed to '{archiveName}' after successful processing.");
                _logger.LogInformation(
                    "Blob '{Source}' archived as '{Archive}'.", blobName, archiveName);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Archive rename failed for '{BlobName}'.", blobName);
                await jobService.UpdateStatusAsync(job.Id, BlobWatcherJobStatus.Completed,
                    logEntry: $"Processing complete but archive rename to '{archiveName}' failed: {ex.Message}");
            }
        }
        else
        {
            await jobService.UpdateStatusAsync(job.Id, BlobWatcherJobStatus.Completed,
                logEntry: "Processing completed successfully (no archive pattern configured).");
        }

        _logger.LogInformation("Completed processing blob '{BlobName}'.", blobName);

        // ── 5. Success notification ───────────────────────────────────────────
        if (config.NotifyOnSuccess)
        {
            await TrySendEmailAsync(mailService, config.NotificationEmail,
                $"[AgentifFlow] File Processed Successfully: {blobName}",
                $"The AgentifFlow agent has successfully processed the file \"{blobName}\".\n\n" +
                $"Container: {config.BlobContainerName}\n" +
                (config.SqlPushEnabled ? $"Rows inserted: {(await jobService.GetByIdAsync(job.Id))?.RowsInserted ?? 0}\n" : "") +
                $"Processed at: {DateTime.UtcNow:u}",
                $"success [{blobName}]");
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

        var csvHeaders = SplitCsvLine(lines[0]);

        // ── Column mapping (Agent Designer) ───────────────────────────────────
        // When SqlColumnMappingJson is configured, map CSV source column names to
        // SQL target column names.  Any CSV column not present in the mapping is
        // still inserted using its original name.
        Dictionary<string, string> colMap = new(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(config.SqlColumnMappingJson))
        {
            try
            {
                var mappings = System.Text.Json.JsonSerializer.Deserialize<List<ColumnMapEntry>>(
                    config.SqlColumnMappingJson,
                    new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                if (mappings is not null)
                    foreach (var m in mappings)
                        if (!string.IsNullOrWhiteSpace(m.Source) && !string.IsNullOrWhiteSpace(m.Target))
                            colMap[m.Source] = m.Target;
            }
            catch { /* ignore malformed mapping JSON — fall back to identity mapping */ }
        }

        // Resolve SQL column names from CSV headers using the mapping (identity fallback)
        var sqlColumns = csvHeaders
            .Select(h => colMap.TryGetValue(h, out var mapped) ? mapped : h)
            .ToArray();

        // ── Table name ────────────────────────────────────────────────────────
        // Use the configured target table when set; fall back to a sanitised version
        // of the blob file name.
        var tableName = !string.IsNullOrWhiteSpace(config.SqlTargetTable)
            ? config.SqlTargetTable   // use as-is (e.g. "dbo.MyTable")
            : System.Text.RegularExpressions.Regex
                .Replace(System.IO.Path.GetFileNameWithoutExtension(blobName), @"[^A-Za-z0-9_]", "_");

        using var connection = new Microsoft.Data.SqlClient.SqlConnection(config.SqlConnectionString);
        await connection.OpenAsync();

        // Create the table if it does not exist (simple schema based on mapped column names)
        if (string.IsNullOrWhiteSpace(config.SqlTargetTable))
        {
            // Only auto-create tables that were derived from the file name (no schema prefix)
            var createCols = string.Join(", ", sqlColumns.Select(h => $"[{h}] NVARCHAR(MAX)"));
            var createSql  =
                $"IF NOT EXISTS (SELECT * FROM sysobjects WHERE name='{tableName}' AND xtype='U') " +
                $"CREATE TABLE [{tableName}] ({createCols})";
            using var createCmd = new Microsoft.Data.SqlClient.SqlCommand(createSql, connection);
            await createCmd.ExecuteNonQueryAsync();
        }

        int inserted = 0;
        for (int i = 1; i < lines.Length; i++)
        {
            var cols      = SplitCsvLine(lines[i]);
            var paramList = string.Join(", ", sqlColumns.Select((_, idx) => $"@p{idx}"));
            var colList   = string.Join(", ", sqlColumns.Select(h => $"[{h}]"));
            var insertSql = $"INSERT INTO {tableName} ({colList}) VALUES ({paramList})";

            using var cmd = new Microsoft.Data.SqlClient.SqlCommand(insertSql, connection);
            for (int j = 0; j < csvHeaders.Length; j++)
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

    /// <summary>Represents a single row in the Agent Designer column-mapping table.</summary>
    private sealed class ColumnMapEntry
    {
        public string Source { get; set; } = "";
        public string Target { get; set; } = "";
    }
}
