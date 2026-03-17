using System.ComponentModel.DataAnnotations;

namespace AgentifFlow.Api.Models;

// ── Local job monitor – simulates an external scheduling system (e.g. Azkaban) ──

public enum MonitoredJobStatus
{
    Running,
    Success,
    Failed,
    Skipped,
}

/// <summary>
/// Represents a single run of a data-pipeline job tracked by the built-in
/// Job Monitor endpoint.  Agents with the <see cref="SkillType.ThirdPartyApiIntegration"/>
/// skill can poll <c>GET /api/jobmonitor/status</c> to detect failures, then use the
/// <see cref="SkillType.LogAnalysis"/> skill to analyse the stored logs.
/// </summary>
public class MonitoredJob
{
    [Key]
    public int Id { get; set; }

    [Required, MaxLength(300)]
    public string JobName { get; set; } = string.Empty;

    [MaxLength(200)]
    public string? ProjectName { get; set; }

    public MonitoredJobStatus Status { get; set; } = MonitoredJobStatus.Running;

    [MaxLength(500)]
    public string? FailureReason { get; set; }

    /// <summary>Simulated log content for the job run.</summary>
    public string? Logs { get; set; }

    public DateTime StartedAt { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

// ── DTOs ─────────────────────────────────────────────────────────────────────

public class MonitoredJobDto
{
    public int    Id            { get; set; }
    public string JobName       { get; set; } = string.Empty;
    public string? ProjectName  { get; set; }
    public string Status        { get; set; } = string.Empty;
    public string? FailureReason { get; set; }
    public string? Logs         { get; set; }
    public DateTime StartedAt   { get; set; }
    public DateTime? CompletedAt { get; set; }
    public DateTime UpdatedAt   { get; set; }
}

public class CreateMonitoredJobRequest
{
    [Required, MaxLength(300)]
    public string JobName { get; set; } = string.Empty;

    [MaxLength(200)]
    public string? ProjectName { get; set; }

    /// <summary>Initial status. Defaults to <c>Running</c>.</summary>
    public MonitoredJobStatus Status { get; set; } = MonitoredJobStatus.Running;

    public string? FailureReason { get; set; }
    public string? Logs { get; set; }
}

public class UpdateMonitoredJobRequest
{
    public MonitoredJobStatus? Status { get; set; }
    public string? FailureReason { get; set; }
    public string? Logs { get; set; }
}

/// <summary>
/// Compact health summary returned by <c>GET /api/jobmonitor/status</c>.
/// The ThirdPartyApiIntegration skill can use <c>"hasFailed":true</c> as a
/// <c>FailureIndicator</c> to trigger the Log Analysis workflow.
/// </summary>
public class JobMonitorStatusDto
{
    public bool   HasFailed    { get; set; }
    public int    FailedCount  { get; set; }
    public int    RunningCount { get; set; }
    public int    TotalCount   { get; set; }
    public string Message      { get; set; } = string.Empty;
    public List<MonitoredJobDto> FailedJobs { get; set; } = new();
}
