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
    /// <summary>Sentinel blob name for external-API monitor jobs (no real blob involved).</summary>
    private const string ExternalApiJobBlobName = "[ext-api]";
    private const string ExternalApiContainerName = "[ext]";

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
        var pollDate = DateTime.UtcNow;

        // ── Per-file-target scanning ──────────────────────────────────────────
        if (agent.FileTargets.Count > 0)
        {
            // ── SQL gate: when SqlManagement is active, all required files must be
            //    present before ANY file is pushed to SQL.  Pre-check existence here.
            var effectiveConfig = agentConfig;
            if (agentConfig.SqlManagementActive)
            {
                bool allRequiredPresent = true;
                foreach (var target in agent.FileTargets.Where(t => t.IsRequired))
                {
                    var requiredName = ResolveFilePattern(target, pollDate);
                    bool requiredExists;
                    try { requiredExists = await containerClient.GetBlobClient(requiredName).ExistsAsync(ct); }
                    catch { requiredExists = false; }

                    if (!requiredExists)
                    {
                        allRequiredPresent = false;
                        _logger.LogInformation(
                            "Agent '{Name}': required file '{Blob}' not yet present — SQL push deferred until all required files arrive.",
                            agent.Name, requiredName);
                        break;
                    }
                }

                if (!allRequiredPresent)
                    effectiveConfig = agentConfig with { SkillSqlManagement = false };
            }

            foreach (var target in agent.FileTargets)
            {
                var expectedName = ResolveFilePattern(target, pollDate);

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

                _logger.LogInformation("Agent '{Name}': new file detected: {Blob}", agent.Name, expectedName);
                var job = await jobService.CreateAsync(expectedName, agentConfig.BlobContainerName, agent.Id);
                await ProcessBlobAsync(job, expectedName, containerClient, effectiveConfig,
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

        // ── ThirdParty API checks (runs after blob cycle) ─────────────────────
        if (agentConfig.ThirdPartyApiActive)
            await RunThirdPartyApiChecksAsync(agent, agentConfig, services, db, ct);
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
                        (j.Status == BlobWatcherJobStatus.AwaitingApproval      ||
                         j.Status == BlobWatcherJobStatus.ValidationFailed      ||
                         j.Status == BlobWatcherJobStatus.AwaitingLogConfirmation ||
                         j.Status == BlobWatcherJobStatus.AwaitingPocApproval))
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

            // Log-analysis workflow: advance through the multi-step approval chain
            if (job.Status == BlobWatcherJobStatus.AwaitingLogConfirmation ||
                job.Status == BlobWatcherJobStatus.AwaitingPocApproval)
            {
                bool rejected = System.Text.RegularExpressions.Regex.IsMatch(
                    reply.BodyPreview ?? string.Empty,
                    @"^\s*reject\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Multiline);
                if (rejected)
                {
                    await jobService.UpdateStatusAsync(job.Id, BlobWatcherJobStatus.Rejected,
                        logEntry: $"Rejected by '{reply.From}': " +
                                  $"{reply.BodyPreview?[..Math.Min(200, reply.BodyPreview?.Length ?? 0)]}");
                }
                else
                {
                    await HandleLogAnalysisReplyAsync(job, reply, db, jobService, mailService, ct);
                }
                continue;
            }

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
        if (agentConfig.SqlManagementActive && !string.IsNullOrWhiteSpace(agentConfig.SqlConnectionString))
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
                logEntry: agentConfig.SqlManagementActive
                    ? $"Validation passed ({validationResult.RowCount} rows). SQL push skipped — connection string not configured."
                    : $"Validation passed ({validationResult.RowCount} rows). SQL Management skill is disabled for this agent.");
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
                (agentConfig.SqlManagementActive ? $"Rows inserted: {(await jobService.GetByIdAsync(job.Id))?.RowsInserted ?? 0}\n" : "") +
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
    // ThirdParty API integration + log-analysis workflow
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Calls the configured external API endpoint and, if a failure is detected,
    /// creates a job and triggers the log-analysis notification workflow.
    /// </summary>
    private async Task RunThirdPartyApiChecksAsync(
        Agent agent,
        AgentRunConfig agentConfig,
        IServiceProvider services,
        AgentifFlowDbContext db,
        CancellationToken ct)
    {
        var apiSkill = agent.Skills.FirstOrDefault(s =>
            s.SkillType == Models.SkillType.ThirdPartyApiIntegration.ToString() && s.IsEnabled);

        if (apiSkill?.ConfigJson is null) return;

        ThirdPartyApiConfig? apiConfig;
        try
        {
            apiConfig = System.Text.Json.JsonSerializer.Deserialize<ThirdPartyApiConfig>(
                apiSkill.ConfigJson,
                new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch { return; }

        if (string.IsNullOrWhiteSpace(apiConfig?.EndpointUrl)) return;

        var jobService = services.GetRequiredService<IBlobWatcherJobService>();

        // Use a hash of the endpoint URL as the sentinel blob name so the value
        // stays within the BlobName VARCHAR(500) column limit.
        var urlHash = Convert.ToHexString(
            System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes(apiConfig.EndpointUrl)))[..16];
        var sentinelBlobName = ExternalApiJobBlobName + urlHash;
        if (await jobService.ExistsAsync(sentinelBlobName, agentConfig.BlobContainerName ?? ExternalApiContainerName))
            return;

        string responseBody;
        try
        {
            var http = services.GetRequiredService<IHttpClientFactory>().CreateClient();

            switch (apiConfig.AuthType?.ToUpperInvariant())
            {
                case "BEARER" when !string.IsNullOrWhiteSpace(apiConfig.AuthToken):
                    http.DefaultRequestHeaders.Authorization =
                        new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiConfig.AuthToken);
                    break;
                case "APIKEY" when !string.IsNullOrWhiteSpace(apiConfig.AuthToken) &&
                                   !string.IsNullOrWhiteSpace(apiConfig.AuthHeaderName):
                    http.DefaultRequestHeaders.Add(apiConfig.AuthHeaderName, apiConfig.AuthToken);
                    break;
                case "BASIC" when !string.IsNullOrWhiteSpace(apiConfig.AuthToken):
                    http.DefaultRequestHeaders.Authorization =
                        new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", apiConfig.AuthToken);
                    break;
            }

            if (apiConfig.RequestMethod?.Equals("POST", StringComparison.OrdinalIgnoreCase) == true)
            {
                var payload = apiConfig.RequestPayloadTemplate ?? "{}";
                var content = new StringContent(payload, System.Text.Encoding.UTF8, "application/json");
                var postResp = await http.PostAsync(apiConfig.EndpointUrl, content, ct);
                responseBody = await postResp.Content.ReadAsStringAsync(ct);
            }
            else
            {
                responseBody = await http.GetStringAsync(apiConfig.EndpointUrl, ct);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Agent '{Name}': ThirdPartyApi call to '{Url}' failed.", agent.Name, apiConfig.EndpointUrl);
            return;
        }

        // Determine whether the response indicates a failure
        bool isFailure = false;
        if (!string.IsNullOrEmpty(apiConfig.FailureIndicator) &&
            responseBody.Contains(apiConfig.FailureIndicator, StringComparison.OrdinalIgnoreCase))
            isFailure = true;
        else if (!string.IsNullOrEmpty(apiConfig.SuccessIndicator) &&
                 !responseBody.Contains(apiConfig.SuccessIndicator, StringComparison.OrdinalIgnoreCase))
            isFailure = true;

        if (!isFailure)
        {
            _logger.LogDebug("Agent '{Name}': ThirdPartyApi check passed for '{Url}'.", agent.Name, apiConfig.EndpointUrl);
            return;
        }

        _logger.LogWarning("Agent '{Name}': ThirdPartyApi failure detected at '{Url}'.", agent.Name, apiConfig.EndpointUrl);

        var job = await jobService.CreateAsync(
            sentinelBlobName,
            agentConfig.BlobContainerName ?? ExternalApiContainerName,
            agent.Id);

        if (agentConfig.LogAnalysisActive)
            await AnalyzeLogsAndNotifyAsync(job, responseBody, agent, agentConfig, services, ct);
        else
            await jobService.UpdateStatusAsync(job.Id, BlobWatcherJobStatus.Failed,
                logEntry: "ThirdParty API failure detected. Log Analysis skill is not enabled.\n" +
                          $"Response preview:\n{responseBody[..Math.Min(500, responseBody.Length)]}");
    }

    /// <summary>
    /// Sends log content to the LLM for root-cause analysis, then emails the operator
    /// asking them to confirm whether the analysis is correct (step 1 of the workflow).
    /// </summary>
    private async Task AnalyzeLogsAndNotifyAsync(
        BlobWatcherJob job,
        string logContent,
        Agent agent,
        AgentRunConfig agentConfig,
        IServiceProvider services,
        CancellationToken ct)
    {
        var jobService  = services.GetRequiredService<IBlobWatcherJobService>();
        var llmService  = services.GetRequiredService<ILlmService>();
        var mailService = services.GetRequiredService<IGraphMailService>();

        // Resolve LogAnalysis skill config
        var logSkill = agent.Skills.FirstOrDefault(s =>
            s.SkillType == Models.SkillType.LogAnalysis.ToString() && s.IsEnabled);
        LogAnalysisConfig? logConfig = null;
        if (logSkill?.ConfigJson is not null)
            try
            {
                logConfig = System.Text.Json.JsonSerializer.Deserialize<LogAnalysisConfig>(
                    logSkill.ConfigJson,
                    new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            }
            catch { /* use defaults */ }

        // Analyse via LLM
        string analysisResult;
        try
        {
            var truncatedLog = logContent.Length > 4000
                ? logContent[..4000] + "\n...[truncated]"
                : logContent;

            var prompt = string.IsNullOrWhiteSpace(logConfig?.AnalysisPrompt)
                ? "Analyze the following job failure log and identify the root cause. " +
                  "Focus on file naming mismatches, missing files, or configuration errors. " +
                  "Provide a concise summary: (1) what failed, (2) why it failed, (3) what correction is needed."
                : logConfig.AnalysisPrompt;

            analysisResult = await llmService.SummarizeAsync($"{prompt}\n\nLog content:\n{truncatedLog}");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Agent '{Name}': LLM log analysis failed.", agent.Name);
            analysisResult = "Automated analysis unavailable. Please review the log manually.";
        }

        // Persist analysis in the job log
        await jobService.UpdateStatusAsync(job.Id, BlobWatcherJobStatus.ValidationFailed,
            logEntry: $"[Log Analysis]\n{analysisResult}\n\n" +
                      $"[Raw Log (first 2000 chars)]\n{logContent[..Math.Min(2000, logContent.Length)]}");

        // Send step-1 confirmation email
        if (string.IsNullOrWhiteSpace(agentConfig.NotificationEmail))
        {
            await jobService.UpdateStatusAsync(job.Id, BlobWatcherJobStatus.Failed,
                logEntry: "Log analysis complete but no notification email is configured — cannot proceed without human confirmation.");
            return;
        }

        var notifRef  = GenerateRef();
        var emailBody =
            $"Agent: {agentConfig.AgentName}\n\n" +
            $"I have analysed the job failure logs and identified the following root cause:\n\n" +
            $"────────────────────────────────────────\n" +
            $"{analysisResult}\n" +
            $"────────────────────────────────────────\n\n" +
            $"Please confirm whether this analysis is correct.\n\n" +
            $"→ Reply with 'Approve' to confirm and proceed to the next step " +
            $"(sending a correction email to the data owner).\n" +
            $"→ Reply with 'Reject' to dismiss this finding.\n\n" +
            $"Reference: {notifRef}";

        bool sent = await TrySendEmailAsync(
            mailService, agentConfig.NotificationEmail,
            $"[AgentifFlow] Log Analysis — {agentConfig.AgentName} [Ref: {notifRef}]",
            emailBody,
            $"log-analysis-confirm [{notifRef}]");

        if (sent)
        {
            await jobService.SetNotificationRefAsync(job.Id, notifRef);
            await jobService.UpdateStatusAsync(job.Id, BlobWatcherJobStatus.AwaitingLogConfirmation,
                logEntry: $"Analysis email sent to {agentConfig.NotificationEmail} [Ref: {notifRef}]. " +
                          $"Awaiting human confirmation.");
        }
        else
        {
            await jobService.UpdateStatusAsync(job.Id, BlobWatcherJobStatus.Failed,
                logEntry: "Log analysis complete but the confirmation email could not be delivered.");
        }
    }

    /// <summary>
    /// Handles email replies for the log-analysis multi-step approval workflow:
    /// <list type="bullet">
    ///   <item><see cref="BlobWatcherJobStatus.AwaitingLogConfirmation"/> → sends a second email
    ///         asking whether to dispatch a correction email to the POC.</item>
    ///   <item><see cref="BlobWatcherJobStatus.AwaitingPocApproval"/> → sends the correction
    ///         email to the configured POC and marks the job Completed.</item>
    /// </list>
    /// </summary>
    private async Task HandleLogAnalysisReplyAsync(
        BlobWatcherJob job,
        EmailMessage reply,
        AgentifFlowDbContext db,
        IBlobWatcherJobService jobService,
        IGraphMailService mailService,
        CancellationToken ct)
    {
        // Reload the agent with skill data
        Agent? agent = null;
        if (job.AgentId.HasValue)
            agent = await db.Agents
                .Include(a => a.Skills)
                .FirstOrDefaultAsync(a => a.Id == job.AgentId.Value, ct);

        LogAnalysisConfig? logConfig = null;
        if (agent is not null)
        {
            var logSkill = agent.Skills.FirstOrDefault(s =>
                s.SkillType == Models.SkillType.LogAnalysis.ToString() && s.IsEnabled);
            if (logSkill?.ConfigJson is not null)
                try
                {
                    logConfig = System.Text.Json.JsonSerializer.Deserialize<LogAnalysisConfig>(
                        logSkill.ConfigJson,
                        new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                }
                catch { /* use defaults */ }
        }

        // ── Step 1 confirmed → ask about sending POC email ─────────────────────
        if (job.Status == BlobWatcherJobStatus.AwaitingLogConfirmation)
        {
            var pocEmail = logConfig?.PocEmail;
            var pocName  = logConfig?.PocName ?? "the data owner";
            var pocRef   = GenerateRef();

            var emailBody =
                $"Thank you for confirming the analysis.\n\n" +
                $"Shall I send a file-correction email to {pocName}" +
                (string.IsNullOrWhiteSpace(pocEmail) ? "" : $" ({pocEmail})") + "?\n\n" +
                $"→ Reply with 'Approve' to send the correction email.\n" +
                $"→ Reply with 'Reject' to close this case without sending.\n\n" +
                $"Reference: {pocRef}";

            var notifEmail = agent?.NotificationEmail;
            if (string.IsNullOrWhiteSpace(notifEmail))
            {
                await jobService.UpdateStatusAsync(job.Id, BlobWatcherJobStatus.Failed,
                    logEntry: "Analysis confirmed but no notification email configured — cannot request POC email approval.");
                return;
            }

            bool sent = false;
            try
            {
                await mailService.SendEmailAsync(new SendEmailRequest
                {
                    To = notifEmail,
                    Subject = $"[AgentifFlow] Send Correction Email? [Ref: {pocRef}]",
                    Body = emailBody, IsHtml = false
                });
                sent = true;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "HandleLogAnalysisReply: could not send POC approval request for job {Id}.", job.Id);
            }

            if (sent)
            {
                await jobService.SetNotificationRefAsync(job.Id, pocRef);
                await jobService.UpdateStatusAsync(job.Id, BlobWatcherJobStatus.AwaitingPocApproval,
                    logEntry: $"Analysis confirmed by '{reply.From}'. " +
                              $"Awaiting approval to send correction email to POC [Ref: {pocRef}].");
            }
            else
            {
                await jobService.UpdateStatusAsync(job.Id, BlobWatcherJobStatus.Failed,
                    logEntry: "Analysis confirmed but the POC-approval email could not be delivered.");
            }

            return;
        }

        // ── Step 2 approved → send correction email to POC ─────────────────────
        if (job.Status == BlobWatcherJobStatus.AwaitingPocApproval)
        {
            var pocEmail = logConfig?.PocEmail;
            if (string.IsNullOrWhiteSpace(pocEmail))
            {
                await jobService.UpdateStatusAsync(job.Id, BlobWatcherJobStatus.Failed,
                    logEntry: "POC email is not configured in the Log Analysis skill — cannot send correction email.");
                return;
            }

            var pocName   = logConfig?.PocName ?? "Team";
            var agentName = agent?.Name ?? "AgentifFlow";

            // Extract the LLM analysis section from the job log
            var logText  = job.LogDetails ?? string.Empty;
            var rawIdx   = logText.IndexOf("[Raw Log", StringComparison.Ordinal);
            var analysis = rawIdx >= 0 ? logText[..rawIdx].Replace("[Log Analysis]", "").Trim() : logText;

            bool sent = false;
            try
            {
                await mailService.SendEmailAsync(new SendEmailRequest
                {
                    To      = pocEmail,
                    Subject = $"[{agentName}] File Correction Required",
                    Body    =
                        $"Dear {pocName},\n\n" +
                        $"Our automated monitoring system has detected an issue with the file delivery " +
                        $"for agent '{agentName}'.\n\n" +
                        $"Root-cause analysis:\n{analysis}\n\n" +
                        $"Please provide the corrected file at your earliest convenience.\n\n" +
                        $"Thank you,\n{agentName}",
                    IsHtml  = false
                });
                sent = true;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "HandleLogAnalysisReply: could not send correction email to POC for job {Id}.", job.Id);
            }

            await jobService.UpdateStatusAsync(
                job.Id,
                sent ? BlobWatcherJobStatus.Completed : BlobWatcherJobStatus.Failed,
                logEntry: sent
                    ? $"File-correction email sent to POC ({pocEmail})."
                    : $"Failed to send correction email to POC ({pocEmail}).");
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

        // Sanitize table and column names to prevent SQL injection.
        // Auto-generated names already go through [^A-Za-z0-9_] regex. Configured
        // names (e.g. "dbo.MyTable") may contain only letters, digits, underscores,
        // dots and brackets — reject anything else.
        var rawTableName = !string.IsNullOrWhiteSpace(agentConfig.SqlTargetTable)
            ? agentConfig.SqlTargetTable
            : System.Text.RegularExpressions.Regex
                .Replace(System.IO.Path.GetFileNameWithoutExtension(blobName), @"[^A-Za-z0-9_]", "_");

        if (!System.Text.RegularExpressions.Regex.IsMatch(rawTableName, @"^[\w\.\[\]]+$"))
            throw new InvalidOperationException(
                $"SqlTargetTable '{rawTableName}' contains characters that are not permitted. " +
                "Use only letters, digits, underscores, dots, and brackets.");

        // Safely quote the table name for use in DDL/DML.
        // Supports schema-qualified names like "dbo.MyTable" or "[dbo].[MyTable]".
        var tableName = rawTableName;

        // Safely escape column identifiers: double any ']' inside bracket-quoted names.
        string QuoteIdentifier(string name) => "[" + name.Replace("]", "]]") + "]";

        using var connection = new Microsoft.Data.SqlClient.SqlConnection(agentConfig.SqlConnectionString);
        await connection.OpenAsync();

        // Create the table if it does not exist (simple schema based on mapped column names)
        if (string.IsNullOrWhiteSpace(agentConfig.SqlTargetTable))
        {
            // Only auto-create tables derived from the file name (no schema prefix).
            // tableName here was built only from [A-Za-z0-9_], so safe to bracket directly.
            var quotedTable = QuoteIdentifier(tableName);
            var createCols  = string.Join(", ", sqlColumns.Select(h => $"{QuoteIdentifier(h)} NVARCHAR(MAX)"));
            using var existsCmd = new Microsoft.Data.SqlClient.SqlCommand(
                "SELECT COUNT(*) FROM sysobjects WHERE name = @tbl AND xtype = 'U'", connection);
            existsCmd.Parameters.AddWithValue("@tbl", tableName);
            var existsResult = await existsCmd.ExecuteScalarAsync();
            var exists = existsResult is not null && (int)existsResult > 0;
            if (!exists)
            {
                var createSql = $"CREATE TABLE {quotedTable} ({createCols})";
                using var createCmd = new Microsoft.Data.SqlClient.SqlCommand(createSql, connection);
                await createCmd.ExecuteNonQueryAsync();
            }
        }

        int inserted = 0;
        for (int i = 1; i < lines.Length; i++)
        {
            var cols      = SplitCsvLine(lines[i]);
            var paramList = string.Join(", ", sqlColumns.Select((_, idx) => $"@p{idx}"));
            var colList   = string.Join(", ", sqlColumns.Select(h => QuoteIdentifier(h)));
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
    /// Resolves a file target's pattern into the expected blob name for the given poll date.
    /// <list type="bullet">
    ///   <item><c>{date}</c> placeholders are replaced with <paramref name="date"/> formatted as <c>yyyyMMdd</c>.</item>
    ///   <item>When <see cref="AgentFileTarget.AppendDate"/> is <c>false</c> and the pattern already
    ///         contains a file extension, the resolved pattern is used as the exact blob name.</item>
    ///   <item>Otherwise the date is appended as a suffix, and <c>.csv</c> is added when no extension is present.</item>
    /// </list>
    /// </summary>
    private static string ResolveFilePattern(AgentFileTarget target, DateTime date)
    {
        var dateStr  = date.ToString("yyyyMMdd");
        var baseName = target.FilePattern.TrimEnd('_');

        // Substitute {date} placeholder (case-insensitive) before any other processing.
        baseName = baseName.Replace("{date}", dateStr, StringComparison.OrdinalIgnoreCase);

        // When AppendDate is false AND the pattern already carries an extension
        // (e.g. "report_{date}.txt" after substitution, or an exact blob name),
        // treat it as the final blob name without further modification.
        if (!target.AppendDate && baseName.Contains('.'))
            return baseName;

        // Legacy AppendDate behaviour: suffix the date and add .csv when no extension.
        return target.AppendDate
            ? $"{baseName}_{dateStr}.csv"
            : $"{baseName}.csv";
    }

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
    /// When an agent has explicit <see cref="AgentSkill"/> records, those override the
    /// monolithic flag properties (backward-compatible fallback for agents with no skills).
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
        bool BlobArchiveAppendDate,
        // ── Skill flags (null = fall back to monolithic property) ──────────────
        bool? SkillEmailMonitoring,
        bool? SkillFileMonitoring,
        bool? SkillDataValidation,
        bool? SkillSqlManagement,
        bool? SkillThirdPartyApi,
        bool? SkillLogAnalysis)
    {
        /// <summary>True if File Monitoring is active (skill-aware, falls back to BlobEnabled / presence of targets).</summary>
        public bool FileMonitoringActive  => SkillFileMonitoring  ?? true;
        /// <summary>True if Data Validation is active (skill-aware, falls back to always-on when configured).</summary>
        public bool DataValidationActive  => SkillDataValidation  ?? true;
        /// <summary>True if SQL Management is active (skill-aware, falls back to monolithic SqlPushEnabled).</summary>
        public bool SqlManagementActive   => SkillSqlManagement   ?? SqlPushEnabled;
        /// <summary>True if Email Monitoring is active (skill-aware, falls back to always-on when mailbox configured).</summary>
        public bool EmailMonitoringActive => SkillEmailMonitoring ?? true;
        /// <summary>True if ThirdParty API Integration is active.</summary>
        public bool ThirdPartyApiActive   => SkillThirdPartyApi   ?? false;
        /// <summary>True if Log Analysis is active.</summary>
        public bool LogAnalysisActive     => SkillLogAnalysis      ?? false;

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
            BlobArchiveAppendDate:    c.BlobArchiveAppendDate,
            SkillEmailMonitoring:     null,
            SkillFileMonitoring:      null,
            SkillDataValidation:      null,
            SkillSqlManagement:       null,
            SkillThirdPartyApi:       null,
            SkillLogAnalysis:         null
        );

        public static AgentRunConfig FromAgent(Agent a, AppConfiguration g)
        {
            // When the agent has explicit skill assignments, resolve them.
            bool? emailSkill = null, fileSkill = null, validSkill = null, sqlSkill = null;
            bool? apiSkill   = null, logSkill  = null;
            if (a.Skills.Any())
            {
                emailSkill = a.Skills.Any(s => s.SkillType == Models.SkillType.EmailMonitoring.ToString()          && s.IsEnabled);
                fileSkill  = a.Skills.Any(s => s.SkillType == Models.SkillType.FileMonitoring.ToString()           && s.IsEnabled);
                validSkill = a.Skills.Any(s => s.SkillType == Models.SkillType.DataValidation.ToString()           && s.IsEnabled);
                sqlSkill   = a.Skills.Any(s => s.SkillType == Models.SkillType.SqlManagement.ToString()            && s.IsEnabled);
                apiSkill   = a.Skills.Any(s => s.SkillType == Models.SkillType.ThirdPartyApiIntegration.ToString() && s.IsEnabled);
                logSkill   = a.Skills.Any(s => s.SkillType == Models.SkillType.LogAnalysis.ToString()              && s.IsEnabled);
            }

            return new(
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
                BlobArchiveAppendDate:    a.BlobArchiveAppendDate,
                SkillEmailMonitoring:     emailSkill,
                SkillFileMonitoring:      fileSkill,
                SkillDataValidation:      validSkill,
                SkillSqlManagement:       sqlSkill,
                SkillThirdPartyApi:       apiSkill,
                SkillLogAnalysis:         logSkill
            );
        }
    }
}
