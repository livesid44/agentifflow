using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace AgentifFlow.Api.Models;

// ── Skill catalogue ────────────────────────────────────────────────────────

/// <summary>The discrete capabilities that can be assigned to any agent.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum SkillType
{
    /// <summary>Watches a Microsoft 365 mailbox for file-arrival notification emails.</summary>
    EmailMonitoring,

    /// <summary>Polls Azure Blob Storage containers for expected files (with per-file-target detection).</summary>
    FileMonitoring,

    /// <summary>Validates ingested CSV data against schema rules and reports errors.</summary>
    DataValidation,

    /// <summary>Pushes validated rows into a SQL Server target table.</summary>
    SqlManagement,

    /// <summary>Calls a configured external REST API endpoint to check status and fetch data.</summary>
    ThirdPartyApiIntegration,

    /// <summary>Analyzes job failure logs via LLM and runs a human-in-the-loop correction workflow.</summary>
    LogAnalysis,
}

/// <summary>Metadata describing a skill (shown in the Control Tower skills catalogue).</summary>
public static class SkillCatalogue
{
    public static readonly IReadOnlyList<SkillInfo> All = new[]
    {
        new SkillInfo(SkillType.EmailMonitoring,         "Email Monitoring",       "Watches a Microsoft 365 mailbox for file-arrival notification emails and triggers the pipeline.",                                                           "email",                "Microsoft.Outlook.com"),
        new SkillInfo(SkillType.FileMonitoring,          "File Monitoring",        "Polls Azure Blob Storage for expected files. Sends alerts when required files are missing.",                                                                "folder",               "Microsoft.Azure.Storage.Blobs"),
        new SkillInfo(SkillType.DataValidation,          "Data Validation",        "Validates ingested CSV rows against configurable schema rules and flags bad data for review.",                                                              "check_box",            "AgentifFlow.Validation"),
        new SkillInfo(SkillType.SqlManagement,           "SQL Management",         "Pushes validated rows into a SQL Server target table, auto-creating the schema when needed.",                                                               "storage",              "Microsoft.Data.SqlClient"),
        new SkillInfo(SkillType.ThirdPartyApiIntegration,"3rd Party Integration",  "Calls a configured external REST API (e.g. Azkaban) to monitor job status and fetch failure logs. Configure endpoint, auth, and response indicators.",     "integration_instructions", "AgentifFlow.ExternalApi"),
        new SkillInfo(SkillType.LogAnalysis,             "Log Analysis",           "Uses AI to analyze failure logs, identifies root causes (e.g. file naming mismatches), confirms with the operator, and sends a correction email to the POC.", "analytics",            "AgentifFlow.LogAnalysis"),
    };
}

public record SkillInfo(SkillType Type, string DisplayName, string Description, string Icon, string Provider);

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

    // ── Schedule ─────────────────────────────────────────────────────────────

    /// <summary>How often (in minutes) the background service runs this agent's poll cycle. Default: 5.</summary>
    [System.ComponentModel.DataAnnotations.Range(1, 1440)]
    public int PollingIntervalMinutes { get; set; } = 5;

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
    public ICollection<AgentSkill>      Skills      { get; set; } = new List<AgentSkill>();
    public ICollection<BlobWatcherJob>  Jobs        { get; set; } = new List<BlobWatcherJob>();
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

/// <summary>Links an agent to a skill and stores optional per-skill configuration.</summary>
public class AgentSkill
{
    [Key]
    public int Id { get; set; }

    public int AgentId { get; set; }

    /// <summary>The type of skill; stored as a string for readability in SQLite.</summary>
    [Required, MaxLength(50)]
    public string SkillType { get; set; } = string.Empty;

    public bool IsEnabled { get; set; } = true;

    /// <summary>Optional JSON bag for per-skill configuration (e.g. custom mailbox for EmailMonitoring).</summary>
    public string? ConfigJson { get; set; }

    public Agent Agent { get; set; } = null!;
}

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
    public int PollingIntervalMinutes { get; set; }
    public bool SqlPushEnabled { get; set; }
    public string? SqlTargetTable { get; set; }
    public string? SqlColumnMappingJson { get; set; }
    public string? BlobArchiveFilePattern { get; set; }
    public bool BlobArchiveAppendDate { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public List<AgentFileTargetDto> FileTargets { get; set; } = new();
    public List<AgentSkillDto> Skills { get; set; } = new();
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
    public int PollingIntervalMinutes { get; set; } = 5;
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
    public int? PollingIntervalMinutes { get; set; }
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

// ── Skill DTOs ────────────────────────────────────────────────────────────────

public class AgentSkillDto
{
    public int Id { get; set; }
    public int AgentId { get; set; }
    public string SkillType { get; set; } = string.Empty;
    public bool IsEnabled { get; set; }
    public string? ConfigJson { get; set; }
}

/// <summary>Replaces all skills for an agent in a single PUT request.</summary>
public class SetSkillsRequest
{
    public List<AgentSkillEntry> Skills { get; set; } = new();
}

public class AgentSkillEntry
{
    [Required, MaxLength(50)]
    public string SkillType { get; set; } = string.Empty;
    public bool IsEnabled { get; set; } = true;
    public string? ConfigJson { get; set; }
}

public class SkillCatalogueDto
{
    public string Type { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Icon { get; set; } = string.Empty;
    public string Provider { get; set; } = string.Empty;
}
