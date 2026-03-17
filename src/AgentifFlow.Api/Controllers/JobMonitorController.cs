using AgentifFlow.Api.Data;
using AgentifFlow.Api.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AgentifFlow.Api.Controllers;

/// <summary>
/// Built-in job monitor that simulates an external scheduling system (e.g. Azkaban).
/// Agents with the <see cref="SkillType.ThirdPartyApiIntegration"/> skill can poll
/// <c>GET /api/jobmonitor/status</c> to detect failures, then pass the response to
/// the <see cref="SkillType.LogAnalysis"/> skill for automated root-cause analysis.
///
/// Suggested agent skill configuration:
/// <code>
/// EndpointUrl    : https://&lt;host&gt;/api/jobmonitor/status
/// RequestMethod  : GET
/// AuthType       : Bearer   (use the API JWT or leave None in dev)
/// FailureIndicator: "hasFailed":true
/// </code>
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
public class JobMonitorController : ControllerBase
{
    private readonly AgentifFlowDbContext _db;
    private readonly ILogger<JobMonitorController> _logger;

    public JobMonitorController(AgentifFlowDbContext db, ILogger<JobMonitorController> logger)
    {
        _db     = db;
        _logger = logger;
    }

    // ── Status endpoint (used as ThirdPartyApiIntegration target) ─────────────

    /// <summary>
    /// Returns a compact health summary that agents can poll.
    /// Configure <c>FailureIndicator</c> as <c>"hasFailed":true</c> in the skill config.
    /// This endpoint is intentionally unauthenticated so the seed demo agent can poll it
    /// without requiring a JWT token configured in the skill settings.
    /// </summary>
    [HttpGet("status")]
    [AllowAnonymous]
    public async Task<ActionResult<JobMonitorStatusDto>> GetStatus(CancellationToken ct)
    {
        var all    = await _db.MonitoredJobs.OrderByDescending(j => j.StartedAt).Take(100).ToListAsync(ct);
        var failed  = all.Where(j => j.Status == MonitoredJobStatus.Failed).ToList();
        var running = all.Where(j => j.Status == MonitoredJobStatus.Running).ToList();

        return Ok(new JobMonitorStatusDto
        {
            HasFailed    = failed.Count > 0,
            FailedCount  = failed.Count,
            RunningCount = running.Count,
            TotalCount   = all.Count,
            Message      = failed.Count > 0
                ? $"{failed.Count} job(s) failed: {string.Join(", ", failed.Select(f => f.JobName))}"
                : "All jobs healthy.",
            FailedJobs   = failed.Select(ToDto).ToList(),
        });
    }

    // ── CRUD ─────────────────────────────────────────────────────────────────

    /// <summary>Lists all tracked job runs, newest first.</summary>
    [HttpGet]
    public async Task<ActionResult<List<MonitoredJobDto>>> GetJobs(
        [FromQuery] string? status = null,
        CancellationToken ct = default)
    {
        var q = _db.MonitoredJobs.AsQueryable();
        if (!string.IsNullOrWhiteSpace(status) &&
            Enum.TryParse<MonitoredJobStatus>(status, ignoreCase: true, out var s))
            q = q.Where(j => j.Status == s);

        var jobs = await q.OrderByDescending(j => j.StartedAt).ToListAsync(ct);
        return Ok(jobs.Select(ToDto).ToList());
    }

    /// <summary>Returns a single job by ID, including its full log content.</summary>
    [HttpGet("{id:int}")]
    public async Task<ActionResult<MonitoredJobDto>> GetJob(int id, CancellationToken ct)
    {
        var job = await _db.MonitoredJobs.FindAsync([id], ct);
        return job is null ? NotFound() : Ok(ToDto(job));
    }

    /// <summary>Returns the stored logs for a specific job (raw text).</summary>
    [HttpGet("{id:int}/logs")]
    public async Task<ActionResult<string>> GetLogs(int id, CancellationToken ct)
    {
        var job = await _db.MonitoredJobs.FindAsync([id], ct);
        if (job is null) return NotFound();
        return Ok(job.Logs ?? string.Empty);
    }

    /// <summary>
    /// Creates a new job run.  Use this to simulate an external job arriving.
    /// Set <c>Status = Failed</c> and populate <c>Logs</c> to simulate a failure
    /// that the Log Analysis skill can detect and analyse.
    /// </summary>
    [HttpPost]
    public async Task<ActionResult<MonitoredJobDto>> CreateJob(
        [FromBody] CreateMonitoredJobRequest req, CancellationToken ct)
    {
        var job = new MonitoredJob
        {
            JobName       = req.JobName,
            ProjectName   = req.ProjectName,
            Status        = req.Status,
            FailureReason = req.FailureReason,
            Logs          = req.Logs,
            StartedAt     = DateTime.UtcNow,
            CompletedAt   = req.Status is MonitoredJobStatus.Success or MonitoredJobStatus.Failed or MonitoredJobStatus.Skipped
                            ? DateTime.UtcNow : null,
            UpdatedAt     = DateTime.UtcNow,
        };

        _db.MonitoredJobs.Add(job);
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("MonitoredJob '{Name}' created with status {Status}.", job.JobName, job.Status);
        return CreatedAtAction(nameof(GetJob), new { id = job.Id }, ToDto(job));
    }

    /// <summary>
    /// Updates the status, failure reason, and/or logs for an existing job.
    /// Use <c>PATCH .../fail</c> shortcut to quickly simulate a failure with
    /// pre-filled sample Azkaban-style logs.
    /// </summary>
    [HttpPatch("{id:int}")]
    public async Task<ActionResult<MonitoredJobDto>> UpdateJob(
        int id, [FromBody] UpdateMonitoredJobRequest req, CancellationToken ct)
    {
        var job = await _db.MonitoredJobs.FindAsync([id], ct);
        if (job is null) return NotFound();

        if (req.Status.HasValue)
        {
            job.Status = req.Status.Value;
            if (req.Status is MonitoredJobStatus.Success or MonitoredJobStatus.Failed or MonitoredJobStatus.Skipped)
                job.CompletedAt = DateTime.UtcNow;
        }
        if (req.FailureReason is not null) job.FailureReason = req.FailureReason;
        if (req.Logs          is not null) job.Logs          = req.Logs;
        job.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync(ct);
        return Ok(ToDto(job));
    }

    /// <summary>
    /// Convenience endpoint: marks the job as <c>Failed</c> and injects a
    /// realistic sample log that demonstrates a file-naming mismatch error.
    /// Useful for demoing the Log Analysis workflow without real infrastructure.
    /// </summary>
    [HttpPatch("{id:int}/fail")]
    public async Task<ActionResult<MonitoredJobDto>> SimulateFail(int id, CancellationToken ct)
    {
        var job = await _db.MonitoredJobs.FindAsync([id], ct);
        if (job is null) return NotFound();

        var sampleLogs = $@"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}] INFO  JobRunner   - Starting job '{job.JobName}'
[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}] INFO  FileCheck   - Scanning landing zone for expected input files
[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}] INFO  FileCheck   - Expected pattern: 344_bi_nerandomilast_targets_([0-9]{{8}})_([0-9]{{8}})_diseases
[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}] WARN  FileCheck   - Pattern NOT matched. Closest candidate found: 344_bi_Jascayd_targets_{DateTime.UtcNow:yyyyMMdd}_{DateTime.UtcNow:yyyyMMdd}_diseases.txt
[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}] ERROR FileCheck   - Required file 344_bi_nerandomilast_targets_{DateTime.UtcNow:yyyyMMdd}_{DateTime.UtcNow:yyyyMMdd}_diseases.txt not found in container
[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}] INFO  FileCheck   - Checked also: events.txt ✓  topics.txt ✓  control.txt ✓  diseases.txt ✗
[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}] ERROR JobRunner   - Job '{job.JobName}' failed: Missing required input file
[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}] INFO  JobRunner   - Job status set to FAILED (exit code 1)";

        job.Status        = MonitoredJobStatus.Failed;
        job.FailureReason = "Missing required input file: 344_bi_nerandomilast_targets file not found (wrong vendor prefix in delivered file).";
        job.Logs          = sampleLogs;
        job.CompletedAt   = DateTime.UtcNow;
        job.UpdatedAt     = DateTime.UtcNow;

        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("MonitoredJob {Id} ('{Name}') set to Failed with sample logs.", job.Id, job.JobName);
        return Ok(ToDto(job));
    }

    /// <summary>Deletes a job run.</summary>
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> DeleteJob(int id, CancellationToken ct)
    {
        var job = await _db.MonitoredJobs.FindAsync([id], ct);
        if (job is null) return NotFound();
        _db.MonitoredJobs.Remove(job);
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    // ── Mapping ───────────────────────────────────────────────────────────────

    private static MonitoredJobDto ToDto(MonitoredJob j) => new()
    {
        Id            = j.Id,
        JobName       = j.JobName,
        ProjectName   = j.ProjectName,
        Status        = j.Status.ToString(),
        FailureReason = j.FailureReason,
        Logs          = j.Logs,
        StartedAt     = j.StartedAt,
        CompletedAt   = j.CompletedAt,
        UpdatedAt     = j.UpdatedAt,
    };
}
