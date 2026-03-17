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
}
