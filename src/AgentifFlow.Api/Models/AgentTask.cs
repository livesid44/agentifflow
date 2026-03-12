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
}
