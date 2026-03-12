namespace AgentifFlow.BlazorWeb.Models;

public class BlobWatcherJobDto
{
    public int Id { get; set; }
    public string BlobName { get; set; } = string.Empty;
    public string? ContainerName { get; set; }
    public string Status { get; set; } = string.Empty;
    public int RetryCount { get; set; }
    public string? ErrorMessage { get; set; }
    public string? LogDetails { get; set; }
    public int? RowsInserted { get; set; }
    public DateTime DetectedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    /// <summary>Unique reference token embedded in the notification email subject.</summary>
    public string? NotificationRef { get; set; }
    /// <summary>Preview of the first reply received from the notification recipient.</summary>
    public string? UserReply { get; set; }
}
