using System.ComponentModel.DataAnnotations;

namespace AgentifFlow.Api.Models;

public enum AgentTaskStatus
{
    Pending,
    InProgress,
    Completed,
    Failed
}

public class AgentTask
{
    [Key]
    public int Id { get; set; }

    [Required]
    [MaxLength(200)]
    public string Title { get; set; } = string.Empty;

    [MaxLength(2000)]
    public string? Description { get; set; }

    public AgentTaskStatus Status { get; set; } = AgentTaskStatus.Pending;

    [MaxLength(200)]
    public string? AssignedTo { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? UpdatedAt { get; set; }

    public DateTime? CompletedAt { get; set; }

    [MaxLength(5000)]
    public string? Result { get; set; }

    // ── Email source tracking ─────────────────────────────────────────────────

    /// <summary>Graph message ID of the email that originated this task.</summary>
    [MaxLength(500)]
    public string? SourceEmailId { get; set; }

    /// <summary>Sender address of the originating email.</summary>
    [MaxLength(300)]
    public string? SourceEmailFrom { get; set; }

    /// <summary>Subject line of the originating email.</summary>
    [MaxLength(500)]
    public string? SourceEmailSubject { get; set; }

    /// <summary>
    /// Graph conversation/thread ID so the agent can monitor replies
    /// and associate follow-up messages with this task.
    /// </summary>
    [MaxLength(500)]
    public string? ConversationId { get; set; }
}
