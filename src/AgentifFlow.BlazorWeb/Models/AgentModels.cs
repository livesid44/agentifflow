namespace AgentifFlow.BlazorWeb.Models;

// ── Skill types ──────────────────────────────────────────────────────────────

public enum SkillType
{
    EmailMonitoring,
    FileMonitoring,
    DataValidation,
    SqlManagement,
    ThirdPartyApiIntegration,
    LogAnalysis,
}

// ── Skill config models (mirror AgentSkillConfig.cs in the API) ──────────────

/// <summary>
/// Configuration for the <see cref="SkillType.ThirdPartyApiIntegration"/> skill.
/// Serialised to/from <see cref="AgentSkillDto.ConfigJson"/>.
/// </summary>
public class ThirdPartyApiConfig
{
    public string? EndpointUrl              { get; set; }
    public string  RequestMethod            { get; set; } = "GET";
    public string  AuthType                 { get; set; } = "None";
    public string? AuthToken                { get; set; }
    public string? AuthHeaderName           { get; set; }
    public string? RequestPayloadTemplate   { get; set; }
    public string? SuccessIndicator         { get; set; }
    public string? FailureIndicator         { get; set; }
}

/// <summary>
/// Configuration for the <see cref="SkillType.LogAnalysis"/> skill.
/// Serialised to/from <see cref="AgentSkillDto.ConfigJson"/>.
/// </summary>
public class LogAnalysisConfig
{
    public string? PocEmail        { get; set; }
    public string? PocName         { get; set; }
    public string? AnalysisPrompt  { get; set; }
}

public class SkillCatalogueDto
{
    public string Type        { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Icon        { get; set; } = string.Empty;
    public string Provider    { get; set; } = string.Empty;
}

public class AgentSkillDto
{
    public int     Id         { get; set; }
    public int     AgentId    { get; set; }
    public string  SkillType  { get; set; } = string.Empty;
    public bool    IsEnabled  { get; set; }
    public string? ConfigJson { get; set; }
}

public class SetSkillsRequest
{
    public List<AgentSkillEntry> Skills { get; set; } = new();
}

public class AgentSkillEntry
{
    public string  SkillType  { get; set; } = string.Empty;
    public bool    IsEnabled  { get; set; } = true;
    public string? ConfigJson { get; set; }
}

// ── Agent DTOs ────────────────────────────────────────────────────────────────

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
    public List<AgentSkillDto>      Skills      { get; set; } = new();
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
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IsEnabled { get; set; } = true;
    public string? BlobContainerName { get; set; }
    public string? NotificationEmail { get; set; }
    public bool NotifyOnSuccess { get; set; } = false;
    public bool NotifyOnFileNotFound { get; set; } = true;
    public bool NotifyOnDataIssue { get; set; } = true;
    public int MaxRetryCount { get; set; } = 3;
    public int AutoRetryIntervalMinutes { get; set; } = 30;
    public bool SqlPushEnabled { get; set; } = false;
    public string? SqlTargetTable { get; set; }
    public string? SqlColumnMappingJson { get; set; }
    public string? BlobArchiveFilePattern { get; set; }
    public bool BlobArchiveAppendDate { get; set; } = true;
    public List<AgentFileTargetRequest> FileTargets { get; set; } = new();
}

public class UpdateAgentRequest
{
    public string? Name { get; set; }
    public string? Description { get; set; }
    public bool? IsEnabled { get; set; }
    public string? BlobContainerName { get; set; }
    public string? NotificationEmail { get; set; }
    public bool? NotifyOnSuccess { get; set; }
    public bool? NotifyOnFileNotFound { get; set; }
    public bool? NotifyOnDataIssue { get; set; }
    public int? MaxRetryCount { get; set; }
    public int? AutoRetryIntervalMinutes { get; set; }
    public bool? SqlPushEnabled { get; set; }
    public string? SqlTargetTable { get; set; }
    public string? SqlColumnMappingJson { get; set; }
    public string? BlobArchiveFilePattern { get; set; }
    public bool? BlobArchiveAppendDate { get; set; }
    public List<AgentFileTargetRequest>? FileTargets { get; set; }
}

public class AgentFileTargetRequest
{
    public string FilePattern { get; set; } = string.Empty;
    public bool AppendDate { get; set; } = true;
    public bool IsRequired { get; set; } = true;
}

