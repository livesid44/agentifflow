namespace AgentifFlow.BlazorWeb.Models;

public class MonitoredJobDto
{
    public int     Id             { get; set; }
    public string  JobName        { get; set; } = string.Empty;
    public string? ProjectName    { get; set; }
    public string  Status         { get; set; } = string.Empty;
    public string? FailureReason  { get; set; }
    public string? Logs           { get; set; }
    public DateTime StartedAt     { get; set; }
    public DateTime? CompletedAt  { get; set; }
    public DateTime UpdatedAt     { get; set; }
}

public class JobMonitorStatusDto
{
    public bool   HasFailed    { get; set; }
    public int    FailedCount  { get; set; }
    public int    RunningCount { get; set; }
    public int    TotalCount   { get; set; }
    public string Message      { get; set; } = string.Empty;
    public List<MonitoredJobDto> FailedJobs { get; set; } = new();
}

public class CreateMonitoredJobRequest
{
    public string  JobName        { get; set; } = string.Empty;
    public string? ProjectName    { get; set; }
    public string  Status         { get; set; } = "Running";
    public string? FailureReason  { get; set; }
    public string? Logs           { get; set; }
}

public class UpdateMonitoredJobRequest
{
    public string? Status         { get; set; }
    public string? FailureReason  { get; set; }
    public string? Logs           { get; set; }
}
