using System.ComponentModel.DataAnnotations;

namespace AgentifFlow.Api.Models;

/// <summary>Persisted application configuration for all Azure service integrations.</summary>
public class AppConfiguration
{
    [Key]
    public int Id { get; set; }

    // ── Graph API / Email ────────────────────────────────────────────────────
    [MaxLength(200)]
    public string? GraphTenantId { get; set; }

    [MaxLength(200)]
    public string? GraphClientId { get; set; }

    [MaxLength(500)]
    public string? GraphClientSecret { get; set; }

    [MaxLength(500)]
    public string? GraphScopes { get; set; }

    /// <summary>
    /// UPN or email address of the mailbox to read from / send as when using
    /// application-level credentials (client credentials flow).
    /// e.g. "inbox@contoso.com" or a user's GUID / UPN.
    /// </summary>
    [MaxLength(300)]
    public string? GraphMailboxAddress { get; set; }

    // ── Azure OpenAI ─────────────────────────────────────────────────────────
    [MaxLength(500)]
    public string? OpenAiEndpoint { get; set; }

    [MaxLength(500)]
    public string? OpenAiApiKey { get; set; }

    [MaxLength(200)]
    public string? OpenAiDeploymentName { get; set; }

    // ── Azure Blob Storage ───────────────────────────────────────────────────
    [MaxLength(1000)]
    public string? BlobStorageConnectionString { get; set; }

    [MaxLength(200)]
    public string? BlobContainerName { get; set; }

    // ── SQL Database ─────────────────────────────────────────────────────────
    [MaxLength(1000)]
    public string? SqlConnectionString { get; set; }

    // ── Agent Flow ───────────────────────────────────────────────────────────
    [MaxLength(200)]
    public string? NotificationEmail { get; set; }

    public int BlobPollIntervalSeconds { get; set; } = 60;

    public int MaxRetryCount { get; set; } = 3;

    public bool AgentFlowEnabled { get; set; } = false;

    /// <summary>
    /// How many minutes to wait before automatically retrying a failed job when no
    /// email reply has been received.  Set to 0 to disable automatic time-based retry
    /// (only an email reply will trigger a re-try in that case).
    /// </summary>
    public int AutoRetryIntervalMinutes { get; set; } = 30;

    // ── Agent Designer — Blob Source ─────────────────────────────────────────
    public bool BlobEnabled { get; set; } = false;

    /// <summary>Email address / mailbox to watch for file notifications (Blob input source).</summary>
    [MaxLength(300)]
    public string? BlobReadEmailId { get; set; }

    /// <summary>When true, today's date is appended to the read-email identifier pattern.</summary>
    public bool BlobReadEmailAppendDate { get; set; } = false;

    /// <summary>Input file name prefix, e.g. "test". Combined with getdate() when BlobInputAppendDate is true.</summary>
    [MaxLength(300)]
    public string? BlobInputFilePattern { get; set; }

    /// <summary>When true, getdate() is appended to the input file pattern, e.g. "test_20260312".</summary>
    public bool BlobInputAppendDate { get; set; } = true;

    /// <summary>Archive file name prefix, e.g. "archive_test". Combined with getdate() when BlobArchiveAppendDate is true.</summary>
    [MaxLength(300)]
    public string? BlobArchiveFilePattern { get; set; }

    /// <summary>When true, getdate() is appended to the archive file pattern.</summary>
    public bool BlobArchiveAppendDate { get; set; } = true;

    // ── Agent Designer — Email Notifications ─────────────────────────────────
    public bool NotifyOnSuccess { get; set; } = false;
    public bool NotifyOnFileNotFound { get; set; } = true;
    public bool NotifyOnDataIssue { get; set; } = true;

    // ── Agent Designer — SQL Push ─────────────────────────────────────────────
    public bool SqlPushEnabled { get; set; } = false;

    /// <summary>Target SQL table name for data push.</summary>
    [MaxLength(300)]
    public string? SqlTargetTable { get; set; }

    /// <summary>JSON array of column mapping objects: [{"source":"col","target":"col"},…]</summary>
    public string? SqlColumnMappingJson { get; set; }

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    [MaxLength(200)]
    public string? UpdatedBy { get; set; }
}

/// <summary>DTO returned to the client — secrets are masked.</summary>
public class AppConfigurationDto
{
    public string? GraphTenantId { get; set; }
    public string? GraphClientId { get; set; }
    public string? GraphClientSecret { get; set; }
    public string? GraphScopes { get; set; }
    public string? GraphMailboxAddress { get; set; }
    public string? OpenAiEndpoint { get; set; }
    public string? OpenAiApiKey { get; set; }
    public string? OpenAiDeploymentName { get; set; }
    public string? BlobStorageConnectionString { get; set; }
    public string? BlobContainerName { get; set; }
    public string? SqlConnectionString { get; set; }
    public string? NotificationEmail { get; set; }
    public int BlobPollIntervalSeconds { get; set; } = 60;
    public int MaxRetryCount { get; set; } = 3;
    public int AutoRetryIntervalMinutes { get; set; } = 30;
    public bool AgentFlowEnabled { get; set; }
    // Agent Designer
    public bool BlobEnabled { get; set; }
    public string? BlobReadEmailId { get; set; }
    public bool BlobReadEmailAppendDate { get; set; }
    public string? BlobInputFilePattern { get; set; }
    public bool BlobInputAppendDate { get; set; } = true;
    public string? BlobArchiveFilePattern { get; set; }
    public bool BlobArchiveAppendDate { get; set; } = true;
    public bool NotifyOnSuccess { get; set; }
    public bool NotifyOnFileNotFound { get; set; } = true;
    public bool NotifyOnDataIssue { get; set; } = true;
    public bool SqlPushEnabled { get; set; }
    public string? SqlTargetTable { get; set; }
    public string? SqlColumnMappingJson { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string? UpdatedBy { get; set; }
}

/// <summary>Result of a connectivity test for an external service.</summary>
public class ConnectivityResult
{
    /// <summary>True when the test succeeded; false when it failed or was not configured.</summary>
    public bool Success { get; set; }

    /// <summary>Human-readable message describing the outcome.</summary>
    public string Message { get; set; } = string.Empty;
}

/// <summary>Request body for updating configuration.</summary>
public class UpdateAppConfigurationRequest
{
    [MaxLength(200)]
    public string? GraphTenantId { get; set; }

    [MaxLength(200)]
    public string? GraphClientId { get; set; }

    [MaxLength(500)]
    public string? GraphClientSecret { get; set; }

    [MaxLength(500)]
    public string? GraphScopes { get; set; }

    [MaxLength(300)]
    public string? GraphMailboxAddress { get; set; }

    [MaxLength(500)]
    public string? OpenAiEndpoint { get; set; }

    [MaxLength(500)]
    public string? OpenAiApiKey { get; set; }

    [MaxLength(200)]
    public string? OpenAiDeploymentName { get; set; }

    [MaxLength(1000)]
    public string? BlobStorageConnectionString { get; set; }

    [MaxLength(200)]
    public string? BlobContainerName { get; set; }

    [MaxLength(1000)]
    public string? SqlConnectionString { get; set; }

    [MaxLength(200)]
    public string? NotificationEmail { get; set; }
    public int? BlobPollIntervalSeconds { get; set; }
    public int? MaxRetryCount { get; set; }
    public int? AutoRetryIntervalMinutes { get; set; }
    public bool? AgentFlowEnabled { get; set; }

    // Agent Designer
    public bool? BlobEnabled { get; set; }
    [MaxLength(300)]
    public string? BlobReadEmailId { get; set; }
    public bool? BlobReadEmailAppendDate { get; set; }
    [MaxLength(300)]
    public string? BlobInputFilePattern { get; set; }
    public bool? BlobInputAppendDate { get; set; }
    [MaxLength(300)]
    public string? BlobArchiveFilePattern { get; set; }
    public bool? BlobArchiveAppendDate { get; set; }
    public bool? NotifyOnSuccess { get; set; }
    public bool? NotifyOnFileNotFound { get; set; }
    public bool? NotifyOnDataIssue { get; set; }
    public bool? SqlPushEnabled { get; set; }
    [MaxLength(300)]
    public string? SqlTargetTable { get; set; }
    public string? SqlColumnMappingJson { get; set; }
}
