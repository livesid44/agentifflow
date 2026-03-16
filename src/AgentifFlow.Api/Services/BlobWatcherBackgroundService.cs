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

                // ── 1. Multi-agent polling ────────────────────────────────────
                var agents = await db.Agents
                    .Include(a => a.FileTargets)
                    .Where(a => a.IsEnabled)
                    .ToListAsync(stoppingToken);

                if (agents.Count > 0)
                {
                    foreach (var agent in agents)
                        await RunAgentAsync(agent, config, scope.ServiceProvider, db, stoppingToken);
                }
                else if (!string.IsNullOrWhiteSpace(config.BlobStorageConnectionString) &&
                         !string.IsNullOrWhiteSpace(config.BlobContainerName))
                {
                    // ── Legacy single-config mode (no agents defined) ─────────
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

            var elapsed  = DateTime.UtcNow - cycleStart;
            var waitTime = TimeSpan.FromSeconds(pollInterval) - elapsed;
            if (waitTime > TimeSpan.Zero)
                await Task.Delay(waitTime, stoppingToken);
        }

        _logger.LogInformation("BlobWatcherBackgroundService stopped.");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Multi-agent polling
    // ──────────────────────────────────────────────────────────────────────────

    private async Task RunAgentAsync(
        Agent agent,
        AppConfiguration globalConfig,
        IServiceProvider services,
        AgentifFlowDbContext db,
        CancellationToken ct)
    {
        var agentConfig = AgentRunConfig.FromAgent(agent, globalConfig);

        if (string.IsNullOrWhiteSpace(agentConfig.BlobStorageConnectionString) ||
            string.IsNullOrWhiteSpace(agentConfig.BlobContainerName))
        {
            _logger.LogWarning("Agent '{Name}' (Id={Id}): blob storage not configured — skipping.", agent.Name, agent.Id);
            return;
        }

        _logger.LogInformation("Running agent '{Name}' (Id={Id}), container='{Container}'",
            agent.Name, agent.Id, agentConfig.BlobContainerName);

        var blobServiceClient = new BlobServiceClient(agentConfig.BlobStorageConnectionString);
        var containerClient   = blobServiceClient.GetBlobContainerClient(agentConfig.BlobContainerName);
        var jobService        = services.GetRequiredService<IBlobWatcherJobService>();
        var csvValidation     = services.GetRequiredService<ICsvValidationService>();
        var llmService        = services.GetRequiredService<ILlmService>();
        var mailService       = services.GetRequiredService<IGraphMailService>();

        int csvFilesDetected = 0;

        // ── Per-file-target scanning ──────────────────────────────────────────
        if (agent.FileTargets.Count > 0)
        {
            foreach (var target in agent.FileTargets)
            {
                var baseName     = target.FilePattern.TrimEnd('_');
                var expectedName = target.AppendDate
                    ? $"{baseName}_{DateTime.UtcNow:yyyyMMdd}.csv"
                    : $"{baseName}.csv";

                var blobClient = containerClient.GetBlobClient(expectedName);
                bool exists;
                try { exists = await blobClient.ExistsAsync(ct); }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Agent '{Name}': error checking blob '{Blob}'.", agent.Name, expectedName);
                    exists = false;
                }

                if (!exists)
                {
                    if (target.IsRequired && agentConfig.NotifyOnFileNotFound)
                        await SendMissingFileNotificationAsync(expectedName, agentConfig, agent.Id, services, ct);
                    else
                        _logger.LogDebug("Agent '{Name}': '{Blob}' not found (optional target).", agent.Name, expectedName);
                    continue;
                }

                csvFilesDetected++;

                if (await jobService.ExistsAsync(expectedName, agentConfig.BlobContainerName))
                    continue;

                _logger.LogInformation("Agent '{Name}': new CSV detected: {Blob}", agent.Name, expectedName);
                var job = await jobService.CreateAsync(expectedName, agentConfig.BlobContainerName, agent.Id);
                await ProcessBlobAsync(job, expectedName, containerClient, agentConfig,
                    jobService, csvValidation, llmService, mailService, ct);
            }
        }
        else
        {
            // No file targets defined — scan all CSVs in the container
            await foreach (BlobItem blob in containerClient.GetBlobsAsync(cancellationToken: ct))
            {
                if (!blob.Name.EndsWith(".csv", StringComparison.OrdinalIgnoreCase)) continue;

                csvFilesDetected++;
                if (await jobService.ExistsAsync(blob.Name, agentConfig.BlobContainerName)) continue;

                var job = await jobService.CreateAsync(blob.Name, agentConfig.BlobContainerName, agent.Id);
                await ProcessBlobAsync(job, blob.Name, containerClient, agentConfig,
                    jobService, csvValidation, llmService, mailService, ct);
            }

            if (csvFilesDetected == 0 && agentConfig.NotifyOnFileNotFound)
                await SendMissingFileNotificationAsync(null, agentConfig, agent.Id, services, ct);
        }

        // ── Auto-retry timer ──────────────────────────────────────────────────
        var timedOut = await db.BlobWatcherJobs
            .Where(j => j.AgentId == agent.Id &&
                        j.Status == BlobWatcherJobStatus.AwaitingApproval &&
                        j.RetryAfterUtc != null && j.RetryAfterUtc <= DateTime.UtcNow)
            .ToListAsync(ct);

        foreach (var tj in timedOut)
        {
            if (tj.RetryCount < agentConfig.MaxRetryCount)
            {
                _logger.LogInformation("Agent '{Name}': auto-retry for job {Id} ('{Blob}')", agent.Name, tj.Id, tj.BlobName);
                await jobService.IncrementRetryAsync(tj.Id);
            }
            else
            {
                _logger.LogWarning("Agent '{Name}': job {Id} exceeded MaxRetryCount — marking Failed.", agent.Name, tj.Id);
                await jobService.UpdateStatusAsync(tj.Id, BlobWatcherJobStatus.Failed,
                    logEntry: $"Max retry count ({agentConfig.MaxRetryCount}) exceeded. Job permanently failed.");
            }
        }

        // ── Re-process Retrying jobs for this agent ───────────────────────────
        var retryJobs = await db.BlobWatcherJobs
            .Where(j => j.AgentId == agent.Id && j.Status == BlobWatcherJobStatus.Retrying)
            .ToListAsync(ct);

        foreach (var rj in retryJobs)
        {
            _logger.LogInformation("Agent '{Name}': re-processing retry job {Id} for blob '{Blob}'", agent.Name, rj.Id, rj.BlobName);
            var rc = string.IsNullOrWhiteSpace(rj.ContainerName)
                ? containerClient
                : blobServiceClient.GetBlobContainerClient(rj.ContainerName);
            await ProcessBlobAsync(rj, rj.BlobName, rc, agentConfig,
                jobService, csvValidation, llmService, mailService, ct);
        }
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Blob storage polling (legacy single-config mode)
    // ──────────────────────────────────────────────────────────────────────────

    private async Task PollBlobStorageAsync(
        IServiceProvider services,
        AppConfiguration config,
        AgentifFlowDbContext db,
        CancellationToken ct)
    {
        var agentConfig = AgentRunConfig.FromAppConfig(config);
        _logger.LogInformation("Polling blob container '{Container}'", config.BlobContainerName);

        var blobServiceClient    = new BlobServiceClient(config.BlobStorageConnectionString);
        var containerClient      = blobServiceClient.GetBlobContainerClient(config.BlobContainerName);
        var jobService           = services.GetRequiredService<IBlobWatcherJobService>();
        var csvValidationService = services.GetRequiredService<ICsvValidationService>();
        var llmService           = services.GetRequiredService<ILlmService>();
        var mailService          = services.GetRequiredService<IGraphMailService>();

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
            await ProcessBlobAsync(job, blob.Name, containerClient, agentConfig,
                jobService, csvValidationService, llmService, mailService, ct);
        }

        // ── Auto-retry: promote AwaitingApproval jobs whose timer has elapsed ─
        var timedOutJobs = await db.BlobWatcherJobs
            .Where(j => j.AgentId == null &&
                        j.Status == BlobWatcherJobStatus.AwaitingApproval &&
                        j.RetryAfterUtc != null &&
                        j.RetryAfterUtc <= DateTime.UtcNow)
            .ToListAsync(ct);

        foreach (var tj in timedOutJobs)
        {
            if (tj.RetryCount < config.MaxRetryCount)
            {
                _logger.LogInformation(
                    "Auto-retry timer elapsed for job {JobId} ('{Blob}') — scheduling retry {N}.",
                    tj.Id, tj.BlobName, tj.RetryCount + 1);
                await jobService.IncrementRetryAsync(tj.Id);
            }
            else
            {
                _logger.LogWarning(
                    "Job {JobId} ('{Blob}') reached MaxRetryCount ({Max}) — marking as Failed.",
                    tj.Id, tj.BlobName, config.MaxRetryCount);
                await jobService.UpdateStatusAsync(tj.Id, BlobWatcherJobStatus.Failed,
                    logEntry: $"Max retry count ({config.MaxRetryCount}) exceeded. Job permanently failed.");
            }
        }

        // Re-process blobs approved for retry (legacy jobs, AgentId == null)
        var retryJobs = await db.BlobWatcherJobs
            .Where(j => j.AgentId == null && j.Status == BlobWatcherJobStatus.Retrying)
            .ToListAsync(ct);

        foreach (var retryJob in retryJobs)
        {
            _logger.LogInformation("Re-processing retry job {JobId} for blob '{Blob}'",
                retryJob.Id, retryJob.BlobName);

            var retryContainer = string.IsNullOrWhiteSpace(retryJob.ContainerName)
                ? containerClient
                : blobServiceClient.GetBlobContainerClient(retryJob.ContainerName);

            await ProcessBlobAsync(retryJob, retryJob.BlobName, retryContainer, agentConfig,
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

    private async Task SendMissingFileNotificationAsync(
        string? expectedFileName,
        AgentRunConfig agentConfig,
        int agentId,
        IServiceProvider services,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(agentConfig.NotificationEmail)) return;

        var jobService  = services.GetRequiredService<IBlobWatcherJobService>();
        var mailService = services.GetRequiredService<IGraphMailService>();

        var blobDesc = expectedFileName ?? "[any .csv]";
        var job      = await jobService.CreateAsync(NoFileBlobName, agentConfig.BlobContainerName, agentId);
        var nref     = GenerateRef();

        _logger.LogInformation(
            "Agent (Id={AgentId}): required file '{File}' not found — sending notification [{Ref}].",
            agentId, blobDesc, nref);

        var subject = $"[AgentifFlow] Required File Not Found: {blobDesc} [Ref: {nref}]";
        var body    =
            $"Agent \"{agentConfig.AgentName}\" polled blob container \"{agentConfig.BlobContainerName}\" " +
            $"but did not find the required file \"{blobDesc}\".\n\n" +
            "Please upload the file, or verify the container name and storage credentials.\n\n" +
            $"The agent will automatically retry in {agentConfig.AutoRetryIntervalMinutes} minutes.\n\n" +
            "Reply to this email once the file is available.\n\n" +
            $"Reference: {nref}";

        bool sent = await TrySendEmailAsync(mailService, agentConfig.NotificationEmail, subject, body, $"missing-file [{nref}]");
        await jobService.SetNotificationRefAsync(job.Id, nref);
        await jobService.UpdateStatusAsync(job.Id,
            sent ? BlobWatcherJobStatus.AwaitingApproval : BlobWatcherJobStatus.Failed,
            logEntry: sent
                ? $"Missing-file notification sent to {agentConfig.NotificationEmail} [Ref: {nref}]."
                : "Notification skipped — email not configured or Graph API unavailable.");
    }

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
        AgentRunConfig agentConfig,
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

            var dlRef        = GenerateRef();
            var retryAfter   = AutoRetryAfter(agentConfig);
            var logMsg       = retryAfter.HasValue
                ? $"Download failed: {ex.Message}. Waiting for reply or auto-retry at {retryAfter:u} [Ref: {dlRef}]."
                : $"Download failed: {ex.Message}. Waiting for reply [Ref: {dlRef}].";

            await jobService.UpdateStatusAsync(job.Id, BlobWatcherJobStatus.AwaitingApproval,
                $"Download failed: {ex.Message}", logMsg, retryAfter);

            if (agentConfig.NotifyOnDataIssue)
            {
                await jobService.SetNotificationRefAsync(job.Id, dlRef);
                var retryNote = retryAfter.HasValue
                    ? $"The agent will also automatically retry at {retryAfter:u} UTC if no reply is received.\n\n"
                    : "";
                await TrySendEmailAsync(mailService, agentConfig.NotificationEmail,
                    $"[AgentifFlow] Failed to Download File: {blobName} [Ref: {dlRef}]",
                    $"The AgentifFlow agent \"{agentConfig.AgentName}\" could not download \"{blobName}\" " +
                    $"from container \"{agentConfig.BlobContainerName}\".\n\n" +
                    $"Error: {ex.Message}\n\n" +
                    "Please verify the file exists and the storage account is accessible.\n\n" +
                    retryNote +
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
                $"\n\nPlease correct the file and re-upload it to container \"{agentConfig.BlobContainerName}\".\n\n" +
                "Reply to this email on this thread once the corrected file has been uploaded — " +
                "the agent monitors this reference and will automatically re-process the file.\n\n" +
                $"Reference: {valRef}";

            bool sent = false;
            if (agentConfig.NotifyOnDataIssue)
            {
                sent = await TrySendEmailAsync(mailService, agentConfig.NotificationEmail,
                    $"[AgentifFlow] CSV Validation Failed: {blobName} [Ref: {valRef}]",
                    emailBody,
                    $"validation-failure [{valRef}]");
            }

            await jobService.SetNotificationRefAsync(job.Id, valRef);
            var valRetryAfter = sent ? AutoRetryAfter(agentConfig) : (DateTime?)null;
            await jobService.UpdateStatusAsync(job.Id,
                sent ? BlobWatcherJobStatus.AwaitingApproval : BlobWatcherJobStatus.ValidationFailed,
                logEntry: sent
                    ? $"Notification sent to {agentConfig.NotificationEmail} [Ref: {valRef}]. Awaiting reply."
                    : "Notification skipped (NotifyOnDataIssue disabled or NotificationEmail not configured).",
                retryAfterUtc: valRetryAfter);
            return;
        }

        // ── 3. Insert into SQL ────────────────────────────────────────────────
        if (agentConfig.SqlPushEnabled && !string.IsNullOrWhiteSpace(agentConfig.SqlConnectionString))
        {
            await jobService.UpdateStatusAsync(job.Id, BlobWatcherJobStatus.Inserting,
                logEntry: $"Validation passed ({validationResult.RowCount} rows). Starting SQL insert.");

            try
            {
                int rowsInserted = await InsertCsvToSqlAsync(csvContent, blobName, agentConfig);
                await jobService.SetRowsInsertedAsync(job.Id, rowsInserted);
                _logger.LogInformation("Completed {BlobName}: {Rows} rows inserted.", blobName, rowsInserted);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "SQL insert failed for {BlobName}", blobName);

                var sqlRef      = GenerateRef();
                var retryAfter  = AutoRetryAfter(agentConfig);
                var logMsg      = retryAfter.HasValue
                    ? $"SQL insert error: {ex.Message}. Waiting for reply or auto-retry at {retryAfter:u} [Ref: {sqlRef}]."
                    : $"SQL insert error: {ex.Message}. Waiting for reply [Ref: {sqlRef}].";

                await jobService.UpdateStatusAsync(job.Id, BlobWatcherJobStatus.AwaitingApproval,
                    $"SQL insert failed: {ex.Message}", logMsg, retryAfter);

                if (agentConfig.NotifyOnDataIssue)
                {
                    await jobService.SetNotificationRefAsync(job.Id, sqlRef);
                    var retryNote = retryAfter.HasValue
                        ? $"The agent will also automatically retry at {retryAfter:u} UTC if no reply is received.\n\n"
                        : "";
                    await TrySendEmailAsync(mailService, agentConfig.NotificationEmail,
                        $"[AgentifFlow] SQL Insert Failed: {blobName} [Ref: {sqlRef}]",
                        $"The CSV file \"{blobName}\" passed validation but could not be inserted into " +
                        $"the database.\n\nError: {ex.Message}\n\n" +
                        "Please check the SQL connection string and ensure the database is reachable.\n\n" +
                        retryNote +
                        $"Reply to this email on this thread once the issue is resolved.\n\nReference: {sqlRef}",
                        $"sql-failure [{sqlRef}]");
                }

                return;
            }
        }
        else
        {
            await jobService.UpdateStatusAsync(job.Id, BlobWatcherJobStatus.Inserting,
                logEntry: agentConfig.SqlPushEnabled
                    ? $"Validation passed ({validationResult.RowCount} rows). SQL push skipped — connection string not configured."
                    : $"Validation passed ({validationResult.RowCount} rows). SQL push is disabled.");
        }

        // ── 4. Archive rename ─────────────────────────────────────────────────
        if (!string.IsNullOrWhiteSpace(agentConfig.BlobArchiveFilePattern))
        {
            var archiveBase = agentConfig.BlobArchiveFilePattern.TrimEnd('_');
            var archiveName = agentConfig.BlobArchiveAppendDate
                ? $"{archiveBase}_{DateTime.UtcNow:yyyyMMdd}.csv"
                : $"{archiveBase}.csv";

            try
            {
                var sourceBlob = container.GetBlobClient(blobName);
                var destBlob   = container.GetBlobClient(archiveName);

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
        if (agentConfig.NotifyOnSuccess)
        {
            await TrySendEmailAsync(mailService, agentConfig.NotificationEmail,
                $"[AgentifFlow] File Processed Successfully: {blobName}",
                $"The AgentifFlow agent \"{agentConfig.AgentName}\" has successfully processed the file \"{blobName}\".\n\n" +
                $"Container: {agentConfig.BlobContainerName}\n" +
                (agentConfig.SqlPushEnabled ? $"Rows inserted: {(await jobService.GetByIdAsync(job.Id))?.RowsInserted ?? 0}\n" : "") +
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
        string csvContent, string blobName, AgentRunConfig agentConfig)
    {
        if (string.IsNullOrWhiteSpace(agentConfig.SqlConnectionString))
            throw new InvalidOperationException("SQL connection string is not configured.");

        var lines = csvContent
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(l => l.TrimEnd('\r'))
            .Where(l => !string.IsNullOrWhiteSpace(l))
            .ToArray();

        if (lines.Length < 2) return 0;

        var csvHeaders = SplitCsvLine(lines[0]);

        Dictionary<string, string> colMap = new(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(agentConfig.SqlColumnMappingJson))
        {
            try
            {
                var mappings = System.Text.Json.JsonSerializer.Deserialize<List<ColumnMapEntry>>(
                    agentConfig.SqlColumnMappingJson,
                    new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                if (mappings is not null)
                    foreach (var m in mappings)
                        if (!string.IsNullOrWhiteSpace(m.Source) && !string.IsNullOrWhiteSpace(m.Target))
                            colMap[m.Source] = m.Target;
            }
            catch { /* ignore malformed mapping JSON */ }
        }

        var sqlColumns = csvHeaders
            .Select(h => colMap.TryGetValue(h, out var mapped) ? mapped : h)
            .ToArray();

        var tableName = !string.IsNullOrWhiteSpace(agentConfig.SqlTargetTable)
            ? agentConfig.SqlTargetTable
            : System.Text.RegularExpressions.Regex
                .Replace(System.IO.Path.GetFileNameWithoutExtension(blobName), @"[^A-Za-z0-9_]", "_");

        using var connection = new Microsoft.Data.SqlClient.SqlConnection(agentConfig.SqlConnectionString);
        await connection.OpenAsync();

        // Create the table if it does not exist (simple schema based on mapped column names)
        if (string.IsNullOrWhiteSpace(agentConfig.SqlTargetTable))
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

    // ──────────────────────────────────────────────────────────────────────────
    // Helpers
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns the UTC timestamp after which the job should be automatically retried,
    /// or <c>null</c> when AutoRetryIntervalMinutes is 0 (timer disabled).
    /// </summary>
    private static DateTime? AutoRetryAfter(AppConfiguration config) =>
        config.AutoRetryIntervalMinutes > 0
            ? DateTime.UtcNow.AddMinutes(config.AutoRetryIntervalMinutes)
            : null;

    private static DateTime? AutoRetryAfter(AgentRunConfig cfg) =>
        cfg.AutoRetryIntervalMinutes > 0
            ? DateTime.UtcNow.AddMinutes(cfg.AutoRetryIntervalMinutes)
            : null;

    // ──────────────────────────────────────────────────────────────────────────
    // AgentRunConfig — per-agent settings abstraction
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Captures all settings required to run a single agent's processing pipeline,
    /// whether derived from a named <see cref="Agent"/> or the legacy <see cref="AppConfiguration"/>.
    /// </summary>
    private sealed record AgentRunConfig(
        int? AgentId,
        string AgentName,
        string? NotificationEmail,
        bool NotifyOnSuccess,
        bool NotifyOnFileNotFound,
        bool NotifyOnDataIssue,
        int MaxRetryCount,
        int AutoRetryIntervalMinutes,
        bool SqlPushEnabled,
        string? SqlConnectionString,
        string? SqlTargetTable,
        string? SqlColumnMappingJson,
        string? BlobContainerName,
        string? BlobStorageConnectionString,
        string? BlobArchiveFilePattern,
        bool BlobArchiveAppendDate)
    {
        public static AgentRunConfig FromAppConfig(AppConfiguration c) => new(
            AgentId:                  null,
            AgentName:                "Default",
            NotificationEmail:        c.NotificationEmail,
            NotifyOnSuccess:          c.NotifyOnSuccess,
            NotifyOnFileNotFound:     c.NotifyOnFileNotFound,
            NotifyOnDataIssue:        c.NotifyOnDataIssue,
            MaxRetryCount:            c.MaxRetryCount,
            AutoRetryIntervalMinutes: c.AutoRetryIntervalMinutes,
            SqlPushEnabled:           c.SqlPushEnabled,
            SqlConnectionString:      c.SqlConnectionString,
            SqlTargetTable:           c.SqlTargetTable,
            SqlColumnMappingJson:     c.SqlColumnMappingJson,
            BlobContainerName:        c.BlobContainerName,
            BlobStorageConnectionString: c.BlobStorageConnectionString,
            BlobArchiveFilePattern:   c.BlobArchiveFilePattern,
            BlobArchiveAppendDate:    c.BlobArchiveAppendDate
        );

        public static AgentRunConfig FromAgent(Agent a, AppConfiguration g) => new(
            AgentId:                  a.Id,
            AgentName:                a.Name,
            NotificationEmail:        a.NotificationEmail ?? g.NotificationEmail,
            NotifyOnSuccess:          a.NotifyOnSuccess,
            NotifyOnFileNotFound:     a.NotifyOnFileNotFound,
            NotifyOnDataIssue:        a.NotifyOnDataIssue,
            MaxRetryCount:            a.MaxRetryCount,
            AutoRetryIntervalMinutes: a.AutoRetryIntervalMinutes,
            SqlPushEnabled:           a.SqlPushEnabled,
            SqlConnectionString:      g.SqlConnectionString,
            SqlTargetTable:           a.SqlTargetTable,
            SqlColumnMappingJson:     a.SqlColumnMappingJson,
            BlobContainerName:        a.BlobContainerName ?? g.BlobContainerName,
            BlobStorageConnectionString: g.BlobStorageConnectionString,
            BlobArchiveFilePattern:   a.BlobArchiveFilePattern ?? g.BlobArchiveFilePattern,
            BlobArchiveAppendDate:    a.BlobArchiveAppendDate
        );
    }
}
