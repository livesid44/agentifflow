using System.ComponentModel.DataAnnotations;

namespace AgentifFlow.Api.Models;

public enum BlobWatcherJobStatus
{
    Detected,
    Validating,
    ValidationFailed,
    AwaitingApproval,
    Inserting,
    Completed,
    Rejected,
    Retrying,
    Failed,
    /// <summary>Reply received from the recipient of a notification email.</summary>
    ReplyReceived
}

/// <summary>Tracks a single CSV file detected in blob storage through its full processing lifecycle.</summary>
public class BlobWatcherJob
{
    [Key]
    public int Id { get; set; }

    [Required, MaxLength(500)]
    public string BlobName { get; set; } = string.Empty;

    [MaxLength(200)]
    public string? ContainerName { get; set; }

    public BlobWatcherJobStatus Status { get; set; } = BlobWatcherJobStatus.Detected;

    public int RetryCount { get; set; } = 0;

    [MaxLength(2000)]
    public string? ErrorMessage { get; set; }

    /// <summary>Newline-delimited log entries for this job.</summary>
    public string? LogDetails { get; set; }

    /// <summary>Number of rows successfully inserted into SQL.</summary>
    public int? RowsInserted { get; set; }

    public DateTime DetectedAt { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// When set, the background service will automatically promote this job to
    /// <see cref="BlobWatcherJobStatus.Retrying"/> once this timestamp is reached,
    /// without waiting for an email reply.  Cleared when a retry is initiated.
    /// </summary>
    public DateTime? RetryAfterUtc { get; set; }

    // ── Notification email tracking ───────────────────────────────────────────

    /// <summary>
    /// Unique reference token embedded in every outbound notification subject line,
    /// e.g. "AGNT-a3b2c4d5".  Used to correlate inbox replies back to this job.
    /// </summary>
    [MaxLength(20)]
    public string? NotificationRef { get; set; }

    /// <summary>
    /// Preview of the first reply received from the notification recipient.
    /// Populated by the inbox reply-polling loop.
    /// </summary>
    [MaxLength(2000)]
    public string? UserReply { get; set; }
}

/// <summary>DTO for returning BlobWatcherJob info to clients.</summary>
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
    public DateTime? RetryAfterUtc { get; set; }
    public string? NotificationRef { get; set; }
    public string? UserReply { get; set; }
}
