using System.ComponentModel.DataAnnotations;

namespace AgentifFlow.Api.Models;

/// <summary>Represents a single configurable agent instance with its own file, SQL, and notification settings.</summary>
public class Agent
{
    [Key]
    public int Id { get; set; }

    [Required, MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(1000)]
    public string? Description { get; set; }

    public bool IsEnabled { get; set; } = true;

    // ── Blob source ─────────────────────────────────────────────────────────

    /// <summary>Overrides the global BlobContainerName from AppConfiguration when set.</summary>
    [MaxLength(200)]
    public string? BlobContainerName { get; set; }

    // ── Notifications ────────────────────────────────────────────────────────

    [MaxLength(300)]
    [System.ComponentModel.DataAnnotations.EmailAddress]
    public string? NotificationEmail { get; set; }

    public bool NotifyOnSuccess { get; set; } = false;
    public bool NotifyOnFileNotFound { get; set; } = true;
    public bool NotifyOnDataIssue { get; set; } = true;

    // ── Retry ────────────────────────────────────────────────────────────────

    [System.ComponentModel.DataAnnotations.Range(0, 100)]
    public int MaxRetryCount { get; set; } = 3;

    [System.ComponentModel.DataAnnotations.Range(0, 1440)]
    public int AutoRetryIntervalMinutes { get; set; } = 30;

    // ── SQL push ─────────────────────────────────────────────────────────────

    public bool SqlPushEnabled { get; set; } = false;

    [MaxLength(300)]
    public string? SqlTargetTable { get; set; }

    public string? SqlColumnMappingJson { get; set; }

    // ── Archive ───────────────────────────────────────────────────────────────

    [MaxLength(300)]
    public string? BlobArchiveFilePattern { get; set; }

    public bool BlobArchiveAppendDate { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    // ── Navigation ────────────────────────────────────────────────────────────

    public ICollection<AgentFileTarget> FileTargets { get; set; } = new List<AgentFileTarget>();
    public ICollection<BlobWatcherJob> Jobs { get; set; } = new List<BlobWatcherJob>();
}

/// <summary>A single file that an agent should look for in blob storage each poll cycle.</summary>
public class AgentFileTarget
{
    [Key]
    public int Id { get; set; }

    public int AgentId { get; set; }

    /// <summary>Base file name pattern, e.g. "daily_sales". Appends date if <see cref="AppendDate"/> is true.</summary>
    [Required, MaxLength(300)]
    public string FilePattern { get; set; } = string.Empty;

    /// <summary>When true, today's date (yyyyMMdd) is appended: "daily_sales_20260316.csv".</summary>
    public bool AppendDate { get; set; } = true;

    /// <summary>When true and the file is not found, a notification email is sent each cycle.</summary>
    public bool IsRequired { get; set; } = true;

    public Agent Agent { get; set; } = null!;
}

// ── DTOs ─────────────────────────────────────────────────────────────────────

public class AgentDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IsEnabled { get; set; }
    public string? BlobContainerName { get; set; }
    public string? NotificationEmail { get; set; }
    public bool NotifyOnSuccess { get; set; }
    public bool NotifyOnFileNotFound { get; set; }
    public bool NotifyOnDataIssue { get; set; }
    public int MaxRetryCount { get; set; }
    public int AutoRetryIntervalMinutes { get; set; }
    public bool SqlPushEnabled { get; set; }
    public string? SqlTargetTable { get; set; }
    public string? SqlColumnMappingJson { get; set; }
    public string? BlobArchiveFilePattern { get; set; }
    public bool BlobArchiveAppendDate { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public List<AgentFileTargetDto> FileTargets { get; set; } = new();
    // ── Aggregated job stats ──────────────────────────────────────────────────
    public int JobsCompleted { get; set; }
    public int JobsInProgress { get; set; }
    public int JobsFailed { get; set; }
    public int JobsAwaitingApproval { get; set; }
    public DateTime? LastActivity { get; set; }
}

public class AgentFileTargetDto
{
    public int Id { get; set; }
    public int AgentId { get; set; }
    public string FilePattern { get; set; } = string.Empty;
    public bool AppendDate { get; set; }
    public bool IsRequired { get; set; }
}

public class CreateAgentRequest
{
    [Required, MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(1000)]
    public string? Description { get; set; }

    public bool IsEnabled { get; set; } = true;

    [MaxLength(200)]
    public string? BlobContainerName { get; set; }

    [MaxLength(300)]
    public string? NotificationEmail { get; set; }

    public bool NotifyOnSuccess { get; set; } = false;
    public bool NotifyOnFileNotFound { get; set; } = true;
    public bool NotifyOnDataIssue { get; set; } = true;
    public int MaxRetryCount { get; set; } = 3;
    public int AutoRetryIntervalMinutes { get; set; } = 30;
    public bool SqlPushEnabled { get; set; } = false;

    [MaxLength(300)]
    public string? SqlTargetTable { get; set; }

    public string? SqlColumnMappingJson { get; set; }

    [MaxLength(300)]
    public string? BlobArchiveFilePattern { get; set; }

    public bool BlobArchiveAppendDate { get; set; } = true;

    public List<AgentFileTargetRequest> FileTargets { get; set; } = new();
}

public class UpdateAgentRequest
{
    [MaxLength(200)]
    public string? Name { get; set; }

    [MaxLength(1000)]
    public string? Description { get; set; }

    public bool? IsEnabled { get; set; }

    [MaxLength(200)]
    public string? BlobContainerName { get; set; }

    [MaxLength(300)]
    public string? NotificationEmail { get; set; }

    public bool? NotifyOnSuccess { get; set; }
    public bool? NotifyOnFileNotFound { get; set; }
    public bool? NotifyOnDataIssue { get; set; }
    public int? MaxRetryCount { get; set; }
    public int? AutoRetryIntervalMinutes { get; set; }
    public bool? SqlPushEnabled { get; set; }

    [MaxLength(300)]
    public string? SqlTargetTable { get; set; }

    public string? SqlColumnMappingJson { get; set; }

    [MaxLength(300)]
    public string? BlobArchiveFilePattern { get; set; }

    public bool? BlobArchiveAppendDate { get; set; }

    /// <summary>When provided, replaces all existing file targets for this agent.</summary>
    public List<AgentFileTargetRequest>? FileTargets { get; set; }
}

public class AgentFileTargetRequest
{
    [Required, MaxLength(300)]
    public string FilePattern { get; set; } = string.Empty;

    public bool AppendDate { get; set; } = true;
    public bool IsRequired { get; set; } = true;
}
