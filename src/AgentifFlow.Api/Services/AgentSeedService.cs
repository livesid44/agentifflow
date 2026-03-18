using AgentifFlow.Api.Data;
using AgentifFlow.Api.Models;
using Microsoft.EntityFrameworkCore;
using System.Data;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace AgentifFlow.Api.Services;

/// <summary>
/// Inserts built-in demo agents on first start.
/// All operations are idempotent — they check for an existing agent by name before inserting.
/// </summary>
public static class AgentSeedService
{
    // File patterns use {date} as a placeholder for the current date (yyyyMMdd).
    // The BlobWatcherBackgroundService resolves {date} at poll time so the patterns
    // remain valid each day without manual updates.
    private static readonly IReadOnlyList<(string Pattern, bool IsRequired)> NerandomilastFileTargets =
    [
        ("344_bi_nerandomilast_targets_{date}_{date}_events.txt",   true),
        ("344_bi_nerandomilast_targets_{date}_{date}_topics.txt",   true),
        ("344_bi_nerandomilast_targets_{date}_{date}_diseases.txt", true),
        // Control file is required — its absence triggers the approval workflow.
        ("344_bi_nerandomilast_targets_{date}_{date}_control.txt",  true),
    ];

    /// <summary>
    /// Deletes stale <see cref="BlobWatcherJob"/> rows at application startup.
    /// <para>
    /// Jobs that are actively awaiting a human reply
    /// (<see cref="BlobWatcherJobStatus.AwaitingApproval"/>,
    /// <see cref="BlobWatcherJobStatus.AwaitingLogConfirmation"/>, or
    /// <see cref="BlobWatcherJobStatus.AwaitingPocApproval"/>) are <em>preserved</em>
    /// so that in-flight email conversations (identified by their <c>[Ref: AGNT-…]</c>
    /// token) continue to work after a restart.
    /// </para>
    /// <para>
    /// All other jobs — including startup-probe entries, pending, processing, failed,
    /// completed, and rejected jobs — are removed so the dashboard always reflects only
    /// the current session's activity.
    /// </para>
    /// </summary>
    public static async Task ClearAllBlobWatcherJobsOnStartupAsync(
        AgentifFlowDbContext db,
        ILogger logger,
        CancellationToken ct = default)
    {
        // Guard: BlobWatcherJobs table may not exist on first boot before migrations run.
        if (db.Database.ProviderName == "Microsoft.EntityFrameworkCore.Sqlite")
        {
            var conn = db.Database.GetDbConnection();
            var shouldClose = conn.State != ConnectionState.Open;
            if (shouldClose) await conn.OpenAsync(ct);
            try
            {
                using var chk = conn.CreateCommand();
                chk.CommandText =
                    "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='BlobWatcherJobs'";
                var tableExists = Convert.ToInt32(await chk.ExecuteScalarAsync(ct)) > 0;
                if (!tableExists)
                {
                    logger.LogDebug("Startup cleanup: 'BlobWatcherJobs' table not found — skipping.");
                    return;
                }
            }
            finally
            {
                if (shouldClose) await conn.CloseAsync();
            }
        }

        // Statuses that represent an active human-reply conversation — preserve these
        // so that email threads can be matched across restarts.
        var awaitingStatuses = new[]
        {
            BlobWatcherJobStatus.AwaitingApproval,
            BlobWatcherJobStatus.AwaitingLogConfirmation,
            BlobWatcherJobStatus.AwaitingPocApproval,
        };

        var preserved = await db.BlobWatcherJobs
            .Where(j => awaitingStatuses.Contains(j.Status))
            .CountAsync(ct);

        var deleted = await db.BlobWatcherJobs
            .Where(j => !awaitingStatuses.Contains(j.Status))
            .ExecuteDeleteAsync(ct);

        if (deleted > 0 || preserved > 0)
        {
            logger.LogInformation(
                "Startup cleanup: deleted {Deleted} BlobWatcherJob(s), preserved {Preserved} awaiting-reply job(s). " +
                "Agents will start fresh this session.", deleted, preserved);
        }
        else
        {
            logger.LogDebug("Startup cleanup: no existing BlobWatcherJobs to clear.");
        }
    }

    /// <summary>
    /// Seeds the "Nerandomilast Target Files Monitor" demo agent if it does not already exist,
    /// or upgrades an existing seeded agent to use dynamic date patterns and the SQL Management skill.
    ///
    /// <para>
    /// File patterns use <c>{date}</c> as a placeholder for today's date (yyyyMMdd) so that
    /// the agent automatically targets the correct daily batch without manual updates:
    /// <c>344_bi_nerandomilast_targets_{date}_{date}_events.txt</c> → e.g.
    /// <c>344_bi_nerandomilast_targets_20260317_20260317_events.txt</c>.
    /// </para>
    ///
    /// <para>
    /// All four files (events, topics, diseases, control) are marked as required.
    /// When any file is missing a notification is sent each poll cycle.
    /// When all files are present and the SQL Management skill is enabled, the data
    /// is automatically pushed to the configured SQL target table.
    /// </para>
    ///
    /// <para>
    /// For testing, set <c>BlobPollIntervalSeconds = 30</c> in Integration Settings
    /// (simulating the 1-hour production cadence with a 30-second cycle).
    /// Upload the four data files to trigger the SQL push; omit any one file to trigger the alert.
    /// </para>
    /// </summary>
    public static async Task SeedNerandomilastAgentAsync(
        AgentifFlowDbContext db,
        ILogger logger,
        CancellationToken ct = default)
    {
        const string agentName = "Nerandomilast Target Files Monitor";

        // Guard: the Agents table may not yet exist on SQLite DBs that were
        // pre-created with EnsureCreated before the AddAgents migration.
        // Attempting to query a non-existent table crashes the startup sequence,
        // so we check first with a raw pragma — the same pattern used by the
        // rest of Program.cs's safety-net helpers.
        if (db.Database.ProviderName == "Microsoft.EntityFrameworkCore.Sqlite")
        {
            var conn = db.Database.GetDbConnection();
            var shouldClose = conn.State != ConnectionState.Open;
            if (shouldClose) await conn.OpenAsync(ct);
            try
            {
                using var chk = conn.CreateCommand();
                chk.CommandText =
                    "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='Agents'";
                var tableExists = Convert.ToInt32(await chk.ExecuteScalarAsync(ct)) > 0;
                if (!tableExists)
                {
                    logger.LogWarning(
                        "Seed: 'Agents' table not found — skipping demo-agent seed. " +
                        "The table will be created by the schema safety-net on next startup.");
                    return;
                }
            }
            finally
            {
                if (shouldClose) await conn.CloseAsync();
            }
        }

        var agent = await db.Agents
            .Include(a => a.FileTargets)
            .Include(a => a.Skills)
            .FirstOrDefaultAsync(a => a.Name == agentName, ct);

        if (agent is null)
        {
            // ── First-time seed ───────────────────────────────────────────────
            logger.LogInformation("Seed: creating demo agent '{Name}'.", agentName);

            agent = new Agent
            {
                Name        = agentName,
                Description =
                    "Monitors daily delivery of clinical-trial target files for compound 344-BI-Nerandomilast. " +
                    "Four files are expected each poll cycle: events, topics, diseases, and control. " +
                    "File names use the current date (yyyyMMdd) so no manual updates are needed. " +
                    "All four files are required — if any is missing a notification email is sent. " +
                    "When all files are present and the SQL Management skill is enabled, the data " +
                    "is automatically pushed to the configured SQL target table. " +
                    "Set the global poll interval to 30 s in Integration Settings to simulate the daily cadence during testing.",
                IsEnabled                = true,
                NotifyOnFileNotFound     = true,
                NotifyOnSuccess          = false,
                NotifyOnDataIssue        = true,
                MaxRetryCount            = 3,
                AutoRetryIntervalMinutes = 1,
                SqlPushEnabled           = false,
                BlobContainerName        = null,
                CreatedAt                = DateTime.UtcNow,
                UpdatedAt                = DateTime.UtcNow,
            };

            foreach (var (pattern, required) in NerandomilastFileTargets)
                agent.FileTargets.Add(new AgentFileTarget
                {
                    FilePattern = pattern,
                    AppendDate  = false,
                    IsRequired  = required,
                });

            agent.Skills = new List<AgentSkill>
            {
                new() { SkillType = nameof(Models.SkillType.EmailMonitoring), IsEnabled = true },
                new() { SkillType = nameof(Models.SkillType.FileMonitoring),  IsEnabled = true },
                new() { SkillType = nameof(Models.SkillType.SqlManagement),   IsEnabled = true },
            };

            db.Agents.Add(agent);
            await db.SaveChangesAsync(ct);

            logger.LogInformation("Seed: demo agent '{Name}' (Id={Id}) created with {Targets} file targets.",
                agent.Name, agent.Id, agent.FileTargets.Count);
        }
        else
        {
            // ── Upgrade existing agent ────────────────────────────────────────
            bool needsSave = false;

            // Upgrade file patterns that still contain hardcoded dates (YYYYMMDD format)
            // or the old time component (e.g. "_2PM_", "_1PM_") to the new {date} placeholder.
            foreach (var target in agent.FileTargets)
            {
                var upgraded = UpgradeFilePattern(target.FilePattern);
                if (upgraded != target.FilePattern)
                {
                    logger.LogInformation(
                        "Seed: upgrading file pattern '{Old}' → '{New}' for agent '{Name}'.",
                        target.FilePattern, upgraded, agentName);
                    target.FilePattern = upgraded;
                    needsSave = true;
                }

                // Ensure all targets are marked required (new requirement: notify on any missing file).
                if (!target.IsRequired)
                {
                    target.IsRequired = true;
                    needsSave = true;
                }
            }

            // Add SqlManagement skill if not already present.
            if (!agent.Skills.Any(s => s.SkillType == nameof(Models.SkillType.SqlManagement)))
            {
                logger.LogInformation(
                    "Seed: adding SqlManagement skill to existing agent '{Name}'.", agentName);
                db.AgentSkills.Add(new AgentSkill
                {
                    AgentId   = agent.Id,
                    SkillType = nameof(Models.SkillType.SqlManagement),
                    IsEnabled = true,
                });
                needsSave = true;
            }

            if (needsSave)
            {
                agent.UpdatedAt = DateTime.UtcNow;
                await db.SaveChangesAsync(ct);
                logger.LogInformation("Seed: demo agent '{Name}' upgraded.", agentName);
            }
            else
            {
                logger.LogDebug("Seed: agent '{Name}' already up to date — skipping.", agentName);
            }
        }
    }

    /// <summary>
    /// Upgrades a legacy hardcoded file pattern to use <c>{date}</c> placeholders.
    /// Replaces 8-digit date sequences (YYYYMMDD) and removes the time component
    /// (e.g. <c>_2PM_</c>, <c>_1PM_</c>, <c>_11AM_</c>) that is no longer part of the pattern.
    /// </summary>
    private static string UpgradeFilePattern(string pattern)
    {
        // Replace sequences of 8 digits (YYYYMMDD) with {date}.
        var result = Regex.Replace(pattern, @"\d{8}", "{date}");

        // Remove the time component, e.g. "_2PM", "_11AM" that may appear between date and suffix.
        // Pattern: underscore + 1-2 digits + AM or PM (case-insensitive).
        result = Regex.Replace(result, @"_\d{1,2}(AM|PM)", string.Empty, RegexOptions.IgnoreCase);

        return result;
    }

    // ── Azkaban Job Monitor seed ──────────────────────────────────────────────

    /// <summary>
    /// Seeds the "Azkaban Job Monitor" demo agent if it does not already exist.
    ///
    /// <para>
    /// This agent demonstrates Use Case 2 end-to-end:
    /// <list type="number">
    ///   <item>The <see cref="SkillType.ThirdPartyApiIntegration"/> skill polls
    ///     <c>GET /api/jobmonitor/status</c> every cycle.</item>
    ///   <item>When a job failure is detected (response contains <c>"hasFailed":true</c>),
    ///     the <see cref="SkillType.LogAnalysis"/> skill fetches the job logs, uses an LLM
    ///     to analyse the root cause and emails the notification address for confirmation.</item>
    ///   <item>Upon human confirmation the agent asks whether to send a file-correction
    ///     email to the POC, then sends it upon approval.</item>
    /// </list>
    /// </para>
    ///
    /// <para>
    /// To run the demo: navigate to <em>Job Monitor</em> in the sidebar, add a job and
    /// click <em>Simulate Failure</em>.  The agent will pick it up on the next poll cycle.
    /// </para>
    /// </summary>
    public static async Task SeedAzkabanJobMonitorAgentAsync(
        AgentifFlowDbContext db,
        ILogger logger,
        CancellationToken ct = default)
    {
        const string agentName = "Azkaban Job Monitor";

        // Guard: Agents table may not exist yet on fresh SQLite databases.
        if (db.Database.ProviderName == "Microsoft.EntityFrameworkCore.Sqlite")
        {
            var conn      = db.Database.GetDbConnection();
            var shouldClose = conn.State != ConnectionState.Open;
            if (shouldClose) await conn.OpenAsync(ct);
            try
            {
                using var chk = conn.CreateCommand();
                chk.CommandText =
                    "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='Agents'";
                var tableExists = Convert.ToInt32(await chk.ExecuteScalarAsync(ct)) > 0;
                if (!tableExists)
                {
                    logger.LogWarning(
                        "Seed: 'Agents' table not found — skipping Azkaban Job Monitor seed.");
                    return;
                }
            }
            finally
            {
                if (shouldClose) await conn.CloseAsync();
            }
        }

        var agent = await db.Agents
            .Include(a => a.Skills)
            .FirstOrDefaultAsync(a => a.Name == agentName, ct);

        if (agent is not null)
        {
            logger.LogDebug("Seed: agent '{Name}' already exists — skipping.", agentName);
            return;
        }

        logger.LogInformation("Seed: creating demo agent '{Name}'.", agentName);

        // ThirdPartyApiIntegration skill config — polls the local Job Monitor API.
        // The EndpointUrl uses localhost:5045 (the default dev port from launchSettings).
        // Users can update this in the agent's Skills settings once they know the actual port.
        var apiConfig = new ThirdPartyApiConfig
        {
            EndpointUrl             = "http://localhost:5045/api/jobmonitor/status",
            RequestMethod           = "GET",
            AuthType                = "None",
            FailureIndicator        = "\"hasFailed\":true",
            SuccessIndicator        = null,
            RequestPayloadTemplate  = null,
        };

        // LogAnalysis skill config — POC details for the file-correction email.
        // Users should update PocEmail / PocName via the agent's Skills settings.
        var logConfig = new LogAnalysisConfig
        {
            PocEmail       = "poc@example.com",
            PocName        = "Data Delivery Team",
            AnalysisPrompt = null,   // uses built-in file-naming mismatch prompt
        };

        var jsonOptions = new JsonSerializerOptions { PropertyNamingPolicy = null };

        agent = new Agent
        {
            Name        = agentName,
            Description =
                "Monitors Azkaban (or any job scheduler) for failed jobs and performs automated " +
                "root-cause analysis. " +
                "Step 1 — ThirdPartyApiIntegration skill polls GET /api/jobmonitor/status each cycle; " +
                "when a failure is detected (response contains \"hasFailed\":true) the job logs are fetched. " +
                "Step 2 — LogAnalysis skill analyses the logs with an LLM, identifies the cause " +
                "(e.g. missing / misnamed files) and emails a summary to the notification address for human confirmation. " +
                "Step 3 — Upon confirmation the agent asks whether to send a correction email to the data POC. " +
                "Step 4 — Upon approval the correction email is sent automatically. " +
                "Demo: use the Job Monitor page to add jobs and click 'Simulate Failure' to trigger the workflow.",
            IsEnabled                = true,
            NotifyOnFileNotFound     = false,
            NotifyOnSuccess          = false,
            NotifyOnDataIssue        = true,
            MaxRetryCount            = 3,
            AutoRetryIntervalMinutes = 1,
            SqlPushEnabled           = false,
            BlobContainerName        = null,
            CreatedAt                = DateTime.UtcNow,
            UpdatedAt                = DateTime.UtcNow,
            Skills = new List<AgentSkill>
            {
                new()
                {
                    SkillType  = nameof(Models.SkillType.EmailMonitoring),
                    IsEnabled  = true,
                    ConfigJson = null,
                },
                new()
                {
                    SkillType  = nameof(Models.SkillType.ThirdPartyApiIntegration),
                    IsEnabled  = true,
                    ConfigJson = JsonSerializer.Serialize(apiConfig, jsonOptions),
                },
                new()
                {
                    SkillType  = nameof(Models.SkillType.LogAnalysis),
                    IsEnabled  = true,
                    ConfigJson = JsonSerializer.Serialize(logConfig, jsonOptions),
                },
            },
        };

        db.Agents.Add(agent);
        await db.SaveChangesAsync(ct);

        logger.LogInformation(
            "Seed: demo agent '{Name}' (Id={Id}) created.", agent.Name, agent.Id);
    }

    // ── Sample BlobWatcher jobs (Dashboard / Jobs tab) ────────────────────────

    /// <summary>
    /// Sentinel blob name prefix used for Azkaban-agent sample external-API check jobs.
    /// </summary>
    private const string SampleExtApiJobBlobName = "[ext-api][sample]";

    /// <summary>
    /// Seeds sample <see cref="BlobWatcherJob"/> rows so the Dashboard Jobs tab is
    /// populated out-of-the-box for demonstration purposes.
    /// <list type="bullet">
    ///   <item>Agent 1 – "Nerandomilast Target Files Monitor": blob-file detection and
    ///   processing jobs representing the daily BI file pipeline.</item>
    ///   <item>Agent 2 – "Azkaban Job Monitor": external-API check jobs representing the
    ///   ThirdPartyApiIntegration skill polling the job-status endpoint.</item>
    /// </list>
    /// This method is idempotent: it skips seeding for an agent when that agent already
    /// has non-probe <see cref="BlobWatcherJob"/> rows so that real runtime jobs are
    /// never overwritten.
    /// </summary>
    public static async Task SeedSampleBlobWatcherJobsAsync(
        AgentifFlowDbContext db,
        ILogger logger,
        CancellationToken ct = default)
    {
        const string nrmAgentName = "Nerandomilast Target Files Monitor";
        const string azkAgentName = "Azkaban Job Monitor";

        var nrmAgent = await db.Agents.FirstOrDefaultAsync(a => a.Name == nrmAgentName, ct);
        var azkAgent = await db.Agents.FirstOrDefaultAsync(a => a.Name == azkAgentName, ct);

        var now = DateTime.UtcNow;

        // ── Agent 1 — Nerandomilast file-watcher sample jobs ──────────────────
        if (nrmAgent is not null)
        {
            bool hasExisting = await db.BlobWatcherJobs.AnyAsync(
                j => j.AgentId == nrmAgent.Id && j.BlobName != StartupProbeBlobName, ct);

            if (!hasExisting)
            {
                var dateStr = now.ToString("yyyyMMdd");
                var prevDate = now.AddDays(-1).ToString("yyyyMMdd");

                var nrmJobs = new List<BlobWatcherJob>
                {
                    // Yesterday's full run — all 4 files arrived, SQL push succeeded
                    new()
                    {
                        BlobName      = $"344_bi_nerandomilast_targets_{prevDate}_{prevDate}_events.txt",
                        ContainerName = "nerandomilast-targets",
                        AgentId       = nrmAgent.Id,
                        Status        = BlobWatcherJobStatus.Completed,
                        RowsInserted  = 12,
                        DetectedAt    = now.AddDays(-1).AddHours(5),
                        CompletedAt   = now.AddDays(-1).AddHours(5).AddMinutes(2),
                        UpdatedAt     = now.AddDays(-1).AddHours(5).AddMinutes(2),
                        LogDetails    = BuildBlobLog(now.AddDays(-1).AddHours(5),
                            ("INFO",  "FileMonitor",  $"Detected blob: 344_bi_nerandomilast_targets_{prevDate}_{prevDate}_events.txt"),
                            ("INFO",  "Validator",    "Running CSV schema validation"),
                            ("INFO",  "Validator",    "Schema validation passed (12 rows, 8 columns)"),
                            ("INFO",  "SqlManager",   "Connecting to SQL target: nerandomilast_targets"),
                            ("INFO",  "SqlManager",   "Inserted 12 rows into [dbo].[nerandomilast_targets]"),
                            ("INFO",  "FileMonitor",  "Job completed successfully"))
                    },
                    new()
                    {
                        BlobName      = $"344_bi_nerandomilast_targets_{prevDate}_{prevDate}_topics.txt",
                        ContainerName = "nerandomilast-targets",
                        AgentId       = nrmAgent.Id,
                        Status        = BlobWatcherJobStatus.Completed,
                        RowsInserted  = 10,
                        DetectedAt    = now.AddDays(-1).AddHours(5).AddMinutes(1),
                        CompletedAt   = now.AddDays(-1).AddHours(5).AddMinutes(3),
                        UpdatedAt     = now.AddDays(-1).AddHours(5).AddMinutes(3),
                        LogDetails    = BuildBlobLog(now.AddDays(-1).AddHours(5).AddMinutes(1),
                            ("INFO",  "FileMonitor",  $"Detected blob: 344_bi_nerandomilast_targets_{prevDate}_{prevDate}_topics.txt"),
                            ("INFO",  "Validator",    "Running CSV schema validation"),
                            ("INFO",  "Validator",    "Schema validation passed (10 rows, 6 columns)"),
                            ("INFO",  "SqlManager",   "Inserted 10 rows into [dbo].[nerandomilast_topics]"),
                            ("INFO",  "FileMonitor",  "Job completed successfully"))
                    },
                    new()
                    {
                        BlobName      = $"344_bi_nerandomilast_targets_{prevDate}_{prevDate}_diseases.txt",
                        ContainerName = "nerandomilast-targets",
                        AgentId       = nrmAgent.Id,
                        Status        = BlobWatcherJobStatus.Completed,
                        RowsInserted  = 7,
                        DetectedAt    = now.AddDays(-1).AddHours(5).AddMinutes(2),
                        CompletedAt   = now.AddDays(-1).AddHours(5).AddMinutes(4),
                        UpdatedAt     = now.AddDays(-1).AddHours(5).AddMinutes(4),
                        LogDetails    = BuildBlobLog(now.AddDays(-1).AddHours(5).AddMinutes(2),
                            ("INFO",  "FileMonitor",  $"Detected blob: 344_bi_nerandomilast_targets_{prevDate}_{prevDate}_diseases.txt"),
                            ("INFO",  "Validator",    "Running CSV schema validation"),
                            ("INFO",  "Validator",    "Schema validation passed (7 rows, 5 columns)"),
                            ("INFO",  "SqlManager",   "Inserted 7 rows into [dbo].[nerandomilast_diseases]"),
                            ("INFO",  "FileMonitor",  "Job completed successfully"))
                    },
                    new()
                    {
                        BlobName      = $"344_bi_nerandomilast_targets_{prevDate}_{prevDate}_control.txt",
                        ContainerName = "nerandomilast-targets",
                        AgentId       = nrmAgent.Id,
                        Status        = BlobWatcherJobStatus.Completed,
                        RowsInserted  = 3,
                        DetectedAt    = now.AddDays(-1).AddHours(5).AddMinutes(3),
                        CompletedAt   = now.AddDays(-1).AddHours(5).AddMinutes(5),
                        UpdatedAt     = now.AddDays(-1).AddHours(5).AddMinutes(5),
                        LogDetails    = BuildBlobLog(now.AddDays(-1).AddHours(5).AddMinutes(3),
                            ("INFO",  "FileMonitor",  $"Detected blob: 344_bi_nerandomilast_targets_{prevDate}_{prevDate}_control.txt"),
                            ("INFO",  "Validator",    "Running control-manifest validation"),
                            ("INFO",  "Validator",    "Control manifest validated: 3 checksum records match"),
                            ("INFO",  "SqlManager",   "Inserted 3 rows into [dbo].[nerandomilast_control]"),
                            ("INFO",  "FileMonitor",  "Job completed successfully"))
                    },
                    // Today's run — events file has validation errors, awaiting approval
                    new()
                    {
                        BlobName      = $"344_bi_nerandomilast_targets_{dateStr}_{dateStr}_events.txt",
                        ContainerName = "nerandomilast-targets",
                        AgentId       = nrmAgent.Id,
                        Status        = BlobWatcherJobStatus.ValidationFailed,
                        ErrorMessage  = "Schema validation failed: column 'CausalityRating' has 2 NULL values (PT-003, PT-009). Expected non-null per protocol NRM-001-v4.2 §8.3.",
                        DetectedAt    = now.AddHours(-3),
                        UpdatedAt     = now.AddHours(-3).AddMinutes(1),
                        LogDetails    = BuildBlobLog(now.AddHours(-3),
                            ("INFO",  "FileMonitor",  $"Detected blob: 344_bi_nerandomilast_targets_{dateStr}_{dateStr}_events.txt"),
                            ("INFO",  "Validator",    "Running CSV schema validation against protocol NRM-001-v4.2 §8.3"),
                            ("WARN",  "Validator",    "Row PT-003: CausalityRating is NULL — expected non-null (SAE record)"),
                            ("WARN",  "Validator",    "Row PT-009: CausalityRating is NULL — expected non-null (SAE record)"),
                            ("ERROR", "Validator",    "Schema validation FAILED: 2 NULL values in required column 'CausalityRating'"),
                            ("INFO",  "FileMonitor",  "Job status set to ValidationFailed — awaiting operator approval"))
                    },
                    // Today's topics file — completed OK
                    new()
                    {
                        BlobName      = $"344_bi_nerandomilast_targets_{dateStr}_{dateStr}_topics.txt",
                        ContainerName = "nerandomilast-targets",
                        AgentId       = nrmAgent.Id,
                        Status        = BlobWatcherJobStatus.Completed,
                        RowsInserted  = 10,
                        DetectedAt    = now.AddHours(-2.5),
                        CompletedAt   = now.AddHours(-2.5).AddMinutes(2),
                        UpdatedAt     = now.AddHours(-2.5).AddMinutes(2),
                        LogDetails    = BuildBlobLog(now.AddHours(-2.5),
                            ("INFO",  "FileMonitor",  $"Detected blob: 344_bi_nerandomilast_targets_{dateStr}_{dateStr}_topics.txt"),
                            ("INFO",  "Validator",    "Schema validation passed (10 rows)"),
                            ("INFO",  "SqlManager",   "Inserted 10 rows into [dbo].[nerandomilast_topics]"),
                            ("INFO",  "FileMonitor",  "Job completed successfully"))
                    },
                };

                db.BlobWatcherJobs.AddRange(nrmJobs);
                logger.LogInformation(
                    "Sample BlobWatcher jobs: seeded {Count} job(s) for agent '{Name}'.",
                    nrmJobs.Count, nrmAgent.Name);
            }
            else
            {
                logger.LogDebug(
                    "Sample BlobWatcher jobs: agent '{Name}' already has jobs — skipping.", nrmAgent.Name);
            }
        }

        // ── Agent 2 — Azkaban Job Monitor sample ext-api check jobs ──────────
        if (azkAgent is not null)
        {
            bool hasExisting = await db.BlobWatcherJobs.AnyAsync(
                j => j.AgentId == azkAgent.Id && j.BlobName != StartupProbeBlobName, ct);

            if (!hasExisting)
            {
                var azkJobs = new List<BlobWatcherJob>
                {
                    // Two successful API poll cycles (no failure detected — logged as passed)
                    new()
                    {
                        BlobName      = $"{SampleExtApiJobBlobName}-pass-1",
                        ContainerName = "[ext]",
                        AgentId       = azkAgent.Id,
                        Status        = BlobWatcherJobStatus.Completed,
                        DetectedAt    = now.AddHours(-6),
                        CompletedAt   = now.AddHours(-6).AddSeconds(3),
                        UpdatedAt     = now.AddHours(-6).AddSeconds(3),
                        LogDetails    = BuildBlobLog(now.AddHours(-6),
                            ("INFO", "ApiMonitor", "Polling Azkaban job-status endpoint: GET /api/jobmonitor/status"),
                            ("INFO", "ApiMonitor", "HTTP 200 OK — response received in 38 ms"),
                            ("INFO", "ApiMonitor", "Response: {\"hasFailed\":false}"),
                            ("INFO", "ApiMonitor", "Success indicator matched — no failures detected"),
                            ("INFO", "ApiMonitor", "API check passed"))
                    },
                    new()
                    {
                        BlobName      = $"{SampleExtApiJobBlobName}-pass-2",
                        ContainerName = "[ext]",
                        AgentId       = azkAgent.Id,
                        Status        = BlobWatcherJobStatus.Completed,
                        DetectedAt    = now.AddHours(-3),
                        CompletedAt   = now.AddHours(-3).AddSeconds(2),
                        UpdatedAt     = now.AddHours(-3).AddSeconds(2),
                        LogDetails    = BuildBlobLog(now.AddHours(-3),
                            ("INFO", "ApiMonitor", "Polling Azkaban job-status endpoint: GET /api/jobmonitor/status"),
                            ("INFO", "ApiMonitor", "HTTP 200 OK — response received in 41 ms"),
                            ("INFO", "ApiMonitor", "Response: {\"hasFailed\":false}"),
                            ("INFO", "ApiMonitor", "Success indicator matched — no failures detected"),
                            ("INFO", "ApiMonitor", "API check passed"))
                    },
                    // One failure detected — job created, log analysis triggered
                    new()
                    {
                        BlobName      = $"{SampleExtApiJobBlobName}-fail-1",
                        ContainerName = "[ext]",
                        AgentId       = azkAgent.Id,
                        Status        = BlobWatcherJobStatus.Failed,
                        ErrorMessage  = "Azkaban job failure detected: GET /api/jobmonitor/status returned {\"hasFailed\":true}. Failure indicator matched.",
                        DetectedAt    = now.AddHours(-1.5),
                        CompletedAt   = now.AddHours(-1.5).AddSeconds(5),
                        UpdatedAt     = now.AddHours(-1.5).AddSeconds(5),
                        LogDetails    = BuildBlobLog(now.AddHours(-1.5),
                            ("INFO",  "ApiMonitor", "Polling Azkaban job-status endpoint: GET /api/jobmonitor/status"),
                            ("INFO",  "ApiMonitor", "HTTP 200 OK — response received in 45 ms"),
                            ("INFO",  "ApiMonitor", "Response: {\"hasFailed\":true}"),
                            ("WARN",  "ApiMonitor", "Failure indicator matched: 'hasFailed' = true"),
                            ("ERROR", "ApiMonitor", "Azkaban job failure detected — creating failure job"),
                            ("INFO",  "ApiMonitor", "ThirdPartyApiIntegration skill: failure job logged"))
                    },
                };

                db.BlobWatcherJobs.AddRange(azkJobs);
                logger.LogInformation(
                    "Sample BlobWatcher jobs: seeded {Count} job(s) for agent '{Name}'.",
                    azkJobs.Count, azkAgent.Name);
            }
            else
            {
                logger.LogDebug(
                    "Sample BlobWatcher jobs: agent '{Name}' already has jobs — skipping.", azkAgent.Name);
            }
        }

        if (nrmAgent is not null || azkAgent is not null)
            await db.SaveChangesAsync(ct);
    }

    private static string BuildBlobLog(
        DateTime baseTime,
        params (string Level, string Component, string Message)[] entries)
        => BuildLog(baseTime, entries);

    // ── Startup probe jobs ────────────────────────────────────────────────────

    /// <summary>
    /// Sentinel blob name used for startup-probe jobs so they are recognisable in the dashboard.
    /// </summary>
    public const string StartupProbeBlobName = "[startup-probe]";

    /// <summary>
    /// Creates startup-probe <see cref="BlobWatcherJob"/> for every enabled agent.
    /// <para>
    /// All agents receive a <see cref="BlobWatcherJobStatus.Detected"/> probe that confirms
    /// the agent started successfully.  When an agent has the
    /// <see cref="SkillType.LogAnalysis"/> skill enabled but Azure OpenAI is not yet
    /// configured the probe log contains a friendly note — the probe is still
    /// <em>Detected</em> (not Failed) so the dashboard does not show a misleading alarm.
    /// The LogAnalysis execution itself will surface the configuration error when it
    /// actually tries to call the AI service.
    /// </para>
    /// A new probe is inserted on every application startup so operators can see when
    /// the service last restarted.
    /// </summary>
    public static async Task SeedStartupJobsAsync(
        AgentifFlowDbContext db,
        ILogger logger,
        CancellationToken ct = default)
    {
        // Read AI config once so we only query the DB once per startup.
        var appConfig   = await db.AppConfigurations.FirstOrDefaultAsync(ct);
        bool aiReady    = !string.IsNullOrWhiteSpace(appConfig?.OpenAiEndpoint)
                       && !string.IsNullOrWhiteSpace(appConfig?.OpenAiApiKey);

        var agents = await db.Agents
            .Include(a => a.Skills)
            .Where(a => a.IsEnabled)
            .ToListAsync(ct);

        if (agents.Count == 0)
        {
            logger.LogDebug("Startup probe: no enabled agents found — skipping.");
            return;
        }

        var now = DateTime.UtcNow;

        foreach (var agent in agents)
        {
            bool needsAi = agent.Skills.Any(s =>
                s.SkillType == nameof(Models.SkillType.LogAnalysis) && s.IsEnabled);

            // All probes are Detected — the startup probe only confirms the agent is alive.
            // If AI is needed but not yet configured a note is added to the log so the
            // operator knows to visit Integration Settings, but the probe itself is not
            // marked Failed (that status is reserved for real runtime failures).
            BlobWatcherJobStatus probeStatus  = BlobWatcherJobStatus.Detected;
            string?              errorMessage = null;
            string               logEntry;

            if (needsAi && !aiReady)
            {
                logEntry = $"[{now:u}] Startup probe — agent '{agent.Name}': " +
                           "agent is enabled and ready. " +
                           "Note: the LogAnalysis skill requires Azure OpenAI to be configured " +
                           "(Configuration → Integration Settings) before automated log analysis will work.";
                logger.LogInformation(
                    "Startup probe: agent '{Name}' (Id={Id}) is ready. " +
                    "Azure OpenAI is not yet configured — LogAnalysis skill will report an error when triggered.",
                    agent.Name, agent.Id);
            }
            else
            {
                logEntry = $"[{now:u}] Startup probe — agent '{agent.Name}': " +
                           "agent is enabled and ready.";
                logger.LogInformation(
                    "Startup probe: agent '{Name}' (Id={Id}) is ready.",
                    agent.Name, agent.Id);
            }

            var job = new BlobWatcherJob
            {
                BlobName      = StartupProbeBlobName,
                ContainerName = null,
                AgentId       = agent.Id,
                Status        = probeStatus,
                ErrorMessage  = errorMessage,
                LogDetails    = logEntry,
                DetectedAt    = now,
                UpdatedAt     = now,
                CompletedAt   = null,
            };

            db.BlobWatcherJobs.Add(job);
        }

        await db.SaveChangesAsync(ct);

        logger.LogInformation(
            "Startup probe: created {Count} probe job(s).", agents.Count);
    }

    // ── Demo monitored jobs ───────────────────────────────────────────────────

    /// <summary>
    /// Seeds sample <see cref="MonitoredJob"/> rows so the Job Monitor tab is populated
    /// out-of-the-box for demonstration purposes.  Jobs reflect realistic pipeline
    /// scenarios for both seeded demo agents:
    /// <list type="bullet">
    ///   <item>Agent 1 – Nerandomilast BI pipeline jobs that produce the daily target files.</item>
    ///   <item>Agent 2 – Azkaban-style orchestration jobs monitored by the Azkaban Job Monitor.</item>
    /// </list>
    /// This method is idempotent: it skips seeding when the <c>MonitoredJobs</c> table
    /// already contains rows so that manually added jobs are never overwritten.
    /// </summary>
    public static async Task SeedDemoMonitoredJobsAsync(
        AgentifFlowDbContext db,
        ILogger logger,
        CancellationToken ct = default)
    {
        // Guard: skip if jobs already exist (idempotent).
        if (await db.MonitoredJobs.AnyAsync(ct))
        {
            logger.LogDebug("Demo monitored jobs: table already has rows — skipping seed.");
            return;
        }

        var now = DateTime.UtcNow;

        // ── Agent 1 — Nerandomilast BI pipeline jobs ──────────────────────────
        // These represent the upstream ETL jobs that generate the daily target
        // files consumed by the "Nerandomilast Target Files Monitor" agent.

        var nrmJobs = new List<MonitoredJob>
        {
            new()
            {
                JobName       = "NRM-BI-ClinicalDB-Extract",
                ProjectName   = "Nerandomilast-Phase2-BI",
                Status        = MonitoredJobStatus.Success,
                StartedAt     = now.AddHours(-5),
                CompletedAt   = now.AddHours(-4.5),
                UpdatedAt     = now.AddHours(-4.5),
                Logs          = BuildLog(now.AddHours(-5),
                    ("INFO",  "JobRunner",    "Starting job 'NRM-BI-ClinicalDB-Extract'"),
                    ("INFO",  "DBConnector",  "Connected to ClinicalTrialDB @ clinicaldb.internal:1433"),
                    ("INFO",  "Extractor",    "Extracting adverse-event records for study NRM-001-Phase2"),
                    ("INFO",  "Extractor",    "Query returned 12 rows (PatientId, EventDate, AECode, …)"),
                    ("INFO",  "FileWriter",   $"Writing 344_bi_nerandomilast_targets_{now:yyyyMMdd}_{now:yyyyMMdd}_events.txt"),
                    ("INFO",  "FileWriter",   "File written: 12 data rows + header"),
                    ("INFO",  "Extractor",    "Extracting disease / indication reference data"),
                    ("INFO",  "Extractor",    "Query returned 7 rows (DiseaseCode, DiseaseName, ICD10Code, …)"),
                    ("INFO",  "FileWriter",   $"Writing 344_bi_nerandomilast_targets_{now:yyyyMMdd}_{now:yyyyMMdd}_diseases.txt"),
                    ("INFO",  "FileWriter",   "File written: 7 data rows + header"),
                    ("INFO",  "Extractor",    "Extracting active discussion topics and action items"),
                    ("INFO",  "Extractor",    "Query returned 10 rows (TopicId, TopicName, Category, Priority, …)"),
                    ("INFO",  "FileWriter",   $"Writing 344_bi_nerandomilast_targets_{now:yyyyMMdd}_{now:yyyyMMdd}_topics.txt"),
                    ("INFO",  "FileWriter",   "File written: 10 data rows + header"),
                    ("INFO",  "JobRunner",    "Job 'NRM-BI-ClinicalDB-Extract' completed successfully (exit code 0)"))
            },
            new()
            {
                JobName       = "NRM-BI-ControlManifest-Generate",
                ProjectName   = "Nerandomilast-Phase2-BI",
                Status        = MonitoredJobStatus.Success,
                StartedAt     = now.AddHours(-4.5),
                CompletedAt   = now.AddHours(-4.4),
                UpdatedAt     = now.AddHours(-4.4),
                Logs          = BuildLog(now.AddHours(-4.5),
                    ("INFO",  "JobRunner",    "Starting job 'NRM-BI-ControlManifest-Generate'"),
                    ("INFO",  "Checksummer",  $"Computing MD5 for 344_bi_nerandomilast_targets_{now:yyyyMMdd}_{now:yyyyMMdd}_events.txt → a3f8d2c91b45e7f06d18c3a22b574e01"),
                    ("INFO",  "Checksummer",  $"Computing MD5 for 344_bi_nerandomilast_targets_{now:yyyyMMdd}_{now:yyyyMMdd}_topics.txt → 7bc945d3e12a0f87c6d45e91a38b20ff"),
                    ("INFO",  "Checksummer",  $"Computing MD5 for 344_bi_nerandomilast_targets_{now:yyyyMMdd}_{now:yyyyMMdd}_diseases.txt → 2e6a1d84c73b950f4a12d8e57c29b3aa"),
                    ("INFO",  "FileWriter",   $"Writing 344_bi_nerandomilast_targets_{now:yyyyMMdd}_{now:yyyyMMdd}_control.txt"),
                    ("INFO",  "FileWriter",   "Control manifest written: 3 data rows + header"),
                    ("INFO",  "JobRunner",    "Job 'NRM-BI-ControlManifest-Generate' completed successfully (exit code 0)"))
            },
            new()
            {
                JobName       = "NRM-BI-BlobUpload",
                ProjectName   = "Nerandomilast-Phase2-BI",
                Status        = MonitoredJobStatus.Success,
                StartedAt     = now.AddHours(-4.4),
                CompletedAt   = now.AddHours(-4.3),
                UpdatedAt     = now.AddHours(-4.3),
                Logs          = BuildLog(now.AddHours(-4.4),
                    ("INFO",  "JobRunner",    "Starting job 'NRM-BI-BlobUpload'"),
                    ("INFO",  "BlobClient",   "Connecting to Azure Blob Storage container 'nerandomilast-targets'"),
                    ("INFO",  "BlobClient",   $"Uploading 344_bi_nerandomilast_targets_{now:yyyyMMdd}_{now:yyyyMMdd}_events.txt (2.3 KB)"),
                    ("INFO",  "BlobClient",   $"Uploading 344_bi_nerandomilast_targets_{now:yyyyMMdd}_{now:yyyyMMdd}_diseases.txt (1.1 KB)"),
                    ("INFO",  "BlobClient",   $"Uploading 344_bi_nerandomilast_targets_{now:yyyyMMdd}_{now:yyyyMMdd}_topics.txt (1.9 KB)"),
                    ("INFO",  "BlobClient",   $"Uploading 344_bi_nerandomilast_targets_{now:yyyyMMdd}_{now:yyyyMMdd}_control.txt (0.4 KB)"),
                    ("INFO",  "BlobClient",   "All 4 files uploaded successfully"),
                    ("INFO",  "JobRunner",    "Job 'NRM-BI-BlobUpload' completed successfully (exit code 0)"))
            },
            new()
            {
                JobName       = "NRM-BI-DataValidation-PostProcess",
                ProjectName   = "Nerandomilast-Phase2-BI",
                Status        = MonitoredJobStatus.Failed,
                FailureReason = "Schema validation failed: column 'CausalityRating' has 2 NULL values in events file (PT-003, PT-009). Expected non-null per protocol NRM-001-v4.2 §8.3.",
                StartedAt     = now.AddHours(-3),
                CompletedAt   = now.AddHours(-2.9),
                UpdatedAt     = now.AddHours(-2.9),
                Logs          = BuildLog(now.AddHours(-3),
                    ("INFO",  "JobRunner",    "Starting job 'NRM-BI-DataValidation-PostProcess'"),
                    ("INFO",  "Validator",    $"Loading 344_bi_nerandomilast_targets_{now:yyyyMMdd}_{now:yyyyMMdd}_events.txt"),
                    ("INFO",  "Validator",    "Running schema checks against protocol NRM-001-v4.2 §8.3"),
                    ("WARN",  "Validator",    "Row PT-003: CausalityRating is NULL — expected non-null (SAE record)"),
                    ("WARN",  "Validator",    "Row PT-009: CausalityRating is NULL — expected non-null (SAE record)"),
                    ("ERROR", "Validator",    "Schema validation FAILED: 2 NULL values in required column 'CausalityRating'"),
                    ("INFO",  "Validator",    "Checking diseases file … OK (7 rows, all required columns populated)"),
                    ("INFO",  "Validator",    "Checking topics file … OK (10 rows)"),
                    ("ERROR", "JobRunner",    "Job 'NRM-BI-DataValidation-PostProcess' failed: schema validation errors in events file"),
                    ("INFO",  "JobRunner",    "Job status set to FAILED (exit code 1)"))
            },
        };

        // ── Agent 2 — Azkaban-style orchestration jobs ────────────────────────
        // These are the jobs that the "Azkaban Job Monitor" agent watches via the
        // ThirdPartyApiIntegration skill (polling GET /api/jobmonitor/status).

        var azkJobs = new List<MonitoredJob>
        {
            new()
            {
                JobName       = "AZK-ClinicalTrial-MasterDataSync",
                ProjectName   = "Azkaban-ClinicalOps",
                Status        = MonitoredJobStatus.Success,
                StartedAt     = now.AddHours(-6),
                CompletedAt   = now.AddHours(-5.8),
                UpdatedAt     = now.AddHours(-5.8),
                Logs          = BuildLog(now.AddHours(-6),
                    ("INFO",  "Azkaban",      "Flow 'AZK-ClinicalTrial-MasterDataSync' started"),
                    ("INFO",  "StageA",       "Syncing patient master records from ClinicalTrialDB → DataWarehouse"),
                    ("INFO",  "StageA",       "172 patient records synced (0 errors)"),
                    ("INFO",  "StageB",       "Syncing site master data (SITE-101 → SITE-106)"),
                    ("INFO",  "StageB",       "6 site records synced"),
                    ("INFO",  "StageC",       "Refreshing study-arm enrollment counts"),
                    ("INFO",  "StageC",       "NerandomilastArm: 87 patients | PlaceboArm: 85 patients"),
                    ("INFO",  "Azkaban",      "Flow completed successfully — duration 12 min"))
            },
            new()
            {
                JobName       = "AZK-AE-Aggregation-Daily",
                ProjectName   = "Azkaban-ClinicalOps",
                Status        = MonitoredJobStatus.Success,
                StartedAt     = now.AddHours(-5),
                CompletedAt   = now.AddHours(-4.7),
                UpdatedAt     = now.AddHours(-4.7),
                Logs          = BuildLog(now.AddHours(-5),
                    ("INFO",  "Azkaban",      "Flow 'AZK-AE-Aggregation-Daily' started"),
                    ("INFO",  "AEProcessor",  "Aggregating adverse-event records for reporting date " + now.ToString("yyyy-MM-dd")),
                    ("INFO",  "AEProcessor",  "Total AEs processed: 12 (9 Mild/Moderate, 2 Severe, 1 SAE)"),
                    ("INFO",  "AEProcessor",  "Causality breakdown: Probable=4, Possible=4, Unrelated=4"),
                    ("INFO",  "AEProcessor",  "Ongoing AEs: 3 (AE-4823×2, AE-6634×1)"),
                    ("INFO",  "Reporter",     "Generating daily AE summary report"),
                    ("INFO",  "Reporter",     "Report written to reports/ae_daily_" + now.ToString("yyyyMMdd") + ".pdf"),
                    ("INFO",  "Azkaban",      "Flow completed successfully — duration 18 min"))
            },
            new()
            {
                JobName       = "AZK-ReportGen-Weekly-Safety",
                ProjectName   = "Azkaban-ClinicalOps",
                Status        = MonitoredJobStatus.Running,
                StartedAt     = now.AddMinutes(-25),
                CompletedAt   = null,
                UpdatedAt     = now.AddMinutes(-5),
                Logs          = BuildLog(now.AddMinutes(-25),
                    ("INFO",  "Azkaban",      "Flow 'AZK-ReportGen-Weekly-Safety' started"),
                    ("INFO",  "DataLoader",   "Loading 4-week AE dataset (2026-02-17 → 2026-03-17)"),
                    ("INFO",  "DataLoader",   "Loaded 47 adverse-event records across 4 weeks"),
                    ("INFO",  "StatEngine",   "Running statistical summaries (incidence rates, confidence intervals)"),
                    ("INFO",  "StatEngine",   "Cough (AE-4823) incidence: 28% NerandomilastArm vs 0% PlaceboArm — flagged for DSMB"),
                    ("INFO",  "PDFRenderer",  "Rendering weekly safety report … [IN PROGRESS]"))
            },
            new()
            {
                JobName       = "AZK-FileTransfer-Landing-Zone",
                ProjectName   = "Azkaban-DataOps",
                Status        = MonitoredJobStatus.Failed,
                FailureReason = "Missing required input file: 344_bi_nerandomilast_targets file not found (wrong vendor prefix in delivered file).",
                StartedAt     = now.AddHours(-2),
                CompletedAt   = now.AddHours(-1.9),
                UpdatedAt     = now.AddHours(-1.9),
                Logs          = BuildLog(now.AddHours(-2),
                    ("INFO",  "Azkaban",      "Flow 'AZK-FileTransfer-Landing-Zone' started"),
                    ("INFO",  "FileCheck",    "Scanning landing zone for expected input files"),
                    ("INFO",  "FileCheck",    $"Expected pattern: 344_bi_nerandomilast_targets_([0-9]{{8}})_([0-9]{{8}})_diseases"),
                    ("WARN",  "FileCheck",    $"Pattern NOT matched. Closest candidate: 344_bi_Jascayd_targets_{now:yyyyMMdd}_{now:yyyyMMdd}_diseases.txt"),
                    ("ERROR", "FileCheck",    $"Required file 344_bi_nerandomilast_targets_{now:yyyyMMdd}_{now:yyyyMMdd}_diseases.txt not found in container"),
                    ("INFO",  "FileCheck",    "Checked also: events.txt ✓  topics.txt ✓  control.txt ✓  diseases.txt ✗"),
                    ("ERROR", "Azkaban",      "Flow 'AZK-FileTransfer-Landing-Zone' failed: Missing required input file"),
                    ("INFO",  "Azkaban",      "Flow status set to FAILED (exit code 1)"))
            },
            new()
            {
                JobName       = "AZK-SQL-TargetTable-Refresh",
                ProjectName   = "Azkaban-DataOps",
                Status        = MonitoredJobStatus.Skipped,
                FailureReason = "Skipped: upstream job 'AZK-FileTransfer-Landing-Zone' failed. SQL refresh requires all 4 target files.",
                StartedAt     = now.AddHours(-1.9),
                CompletedAt   = now.AddHours(-1.9),
                UpdatedAt     = now.AddHours(-1.9),
                Logs          = BuildLog(now.AddHours(-1.9),
                    ("WARN",  "Azkaban",      "Flow 'AZK-SQL-TargetTable-Refresh' skipped — upstream dependency failed"),
                    ("INFO",  "Dependency",   "Upstream: AZK-FileTransfer-Landing-Zone → FAILED"),
                    ("INFO",  "Dependency",   "SQL refresh requires all 4 target files to be present in blob container"),
                    ("WARN",  "Azkaban",      "Flow status set to SKIPPED"))
            },
        };

        db.MonitoredJobs.AddRange(nrmJobs);
        db.MonitoredJobs.AddRange(azkJobs);
        await db.SaveChangesAsync(ct);

        logger.LogInformation(
            "Demo monitored jobs: seeded {NrmCount} Nerandomilast pipeline jobs and {AzkCount} Azkaban jobs.",
            nrmJobs.Count, azkJobs.Count);
    }

    /// <summary>
    /// Builds a multi-line log string from an array of (level, component, message) tuples,
    /// stamping each line with an incrementally offset timestamp.
    /// </summary>
    private static string BuildLog(
        DateTime baseTime,
        params (string Level, string Component, string Message)[] entries)
    {
        var lines = new System.Text.StringBuilder();
        for (int i = 0; i < entries.Length; i++)
        {
            var (level, component, message) = entries[i];
            var ts = baseTime.AddSeconds(i * 8);
            lines.AppendLine(
                $"[{ts:yyyy-MM-dd HH:mm:ss}] {level,-5} {component,-12} - {message}");
        }
        return lines.ToString().TrimEnd();
    }
}
