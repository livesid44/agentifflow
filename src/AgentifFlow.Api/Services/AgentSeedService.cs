using AgentifFlow.Api.Data;
using AgentifFlow.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace AgentifFlow.Api.Services;

/// <summary>
/// Inserts built-in demo agents on first start.
/// All operations are idempotent — they check for an existing agent by name before inserting.
/// </summary>
public static class AgentSeedService
{
    /// <summary>
    /// Seeds the "Nerandomilast Target Files Monitor" demo agent if it does not already exist.
    ///
    /// <para>
    /// This agent demonstrates a real-world hourly batch-delivery use case:
    /// <list type="bullet">
    ///   <item>Four files are expected every hour: <c>events</c>, <c>topics</c>,
    ///         <c>diseases</c>, and <c>control</c>.</item>
    ///   <item>The <c>control</c> file is marked as required.  When it is absent the
    ///         agent sends a human-approval email asking whether a notification should
    ///         be forwarded to the vendor.</item>
    ///   <item>Upon the human clicking <em>Approve</em>, the vendor email is sent
    ///         automatically by the background worker.</item>
    /// </list>
    /// </para>
    ///
    /// <para>
    /// For testing, set <c>BlobPollIntervalSeconds = 30</c> in Integration Settings
    /// (simulating the 1-hour production cadence with a 30-second cycle).
    /// Upload the three data files but omit the control file to trigger the alert.
    /// </para>
    /// </summary>
    public static async Task SeedNerandomilastAgentAsync(
        AgentifFlowDbContext db,
        ILogger logger,
        CancellationToken ct = default)
    {
        const string agentName = "Nerandomilast Target Files Monitor";

        if (await db.Agents.AnyAsync(a => a.Name == agentName, ct))
        {
            logger.LogDebug("Seed: agent '{Name}' already exists — skipping.", agentName);
            return;
        }

        logger.LogInformation("Seed: creating demo agent '{Name}'.", agentName);

        var agent = new Agent
        {
            Name        = agentName,
            Description =
                "Monitors hourly delivery of clinical-trial target files for compound 344-BI-Nerandomilast. " +
                "Four files are expected each cycle: events, topics, diseases, and control. " +
                "When the control file is missing the agent sends a human-approval email; " +
                "upon approval it forwards a vendor notification to the configured address. " +
                "Set the global poll interval to 30 s in Integration Settings to simulate the 1-hour cadence during testing.",
            IsEnabled              = true,
            NotifyOnFileNotFound   = true,
            NotifyOnSuccess        = false,
            NotifyOnDataIssue      = true,
            MaxRetryCount          = 3,
            AutoRetryIntervalMinutes = 1,   // short retry window — suitable for 30 s test cycles
            SqlPushEnabled         = false,
            BlobContainerName      = null,  // inherits global container from Integration Settings
            CreatedAt              = DateTime.UtcNow,
            UpdatedAt              = DateTime.UtcNow,
        };

        // ── File targets ─────────────────────────────────────────────────────
        // Pattern includes the full file name (date + hour baked in for the demo).
        // Set AppendDate = false and include the extension so the watcher treats
        // the pattern as an exact blob name — no automatic date or .csv suffix.
        // Users can clone / edit these targets with real hourly patterns when
        // deploying to production.
        agent.FileTargets = new List<AgentFileTarget>
        {
            new() { FilePattern = "344_bi_nerandomilast_targets_20260311_20260311_2PM_events.txt",
                    AppendDate = false, IsRequired = false },
            new() { FilePattern = "344_bi_nerandomilast_targets_20260311_20260311_2PM_topics.txt",
                    AppendDate = false, IsRequired = false },
            new() { FilePattern = "344_bi_nerandomilast_targets_20260311_20260311_2PM_diseases.txt",
                    AppendDate = false, IsRequired = false },
            // Control file is required — its absence triggers the approval workflow.
            new() { FilePattern = "344_bi_nerandomilast_targets_20260311_20260311_2PM_control.txt",
                    AppendDate = false, IsRequired = true },
        };

        // ── Skills ────────────────────────────────────────────────────────────
        agent.Skills = new List<AgentSkill>
        {
            new() { SkillType = nameof(Models.SkillType.EmailMonitoring), IsEnabled = true },
            new() { SkillType = nameof(Models.SkillType.FileMonitoring),  IsEnabled = true },
        };

        db.Agents.Add(agent);
        await db.SaveChangesAsync(ct);

        logger.LogInformation("Seed: demo agent '{Name}' (Id={Id}) created with {Targets} file targets.",
            agent.Name, agent.Id, agent.FileTargets.Count);
    }
}
