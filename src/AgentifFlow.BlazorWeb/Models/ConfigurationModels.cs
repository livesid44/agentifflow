namespace AgentifFlow.BlazorWeb.Models;

/// <summary>DTO returned by the AgentifFlow API. Secrets arrive masked as ••••••••.</summary>
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

/// <summary>Request body for the PUT /api/configuration endpoint.</summary>
public class UpdateAppConfigurationRequest
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
    public int? BlobPollIntervalSeconds { get; set; }
    public int? MaxRetryCount { get; set; }
    public bool? AgentFlowEnabled { get; set; }
    // Agent Designer
    public bool? BlobEnabled { get; set; }
    public string? BlobReadEmailId { get; set; }
    public bool? BlobReadEmailAppendDate { get; set; }
    public string? BlobInputFilePattern { get; set; }
    public bool? BlobInputAppendDate { get; set; }
    public string? BlobArchiveFilePattern { get; set; }
    public bool? BlobArchiveAppendDate { get; set; }
    public bool? NotifyOnSuccess { get; set; }
    public bool? NotifyOnFileNotFound { get; set; }
    public bool? NotifyOnDataIssue { get; set; }
    public bool? SqlPushEnabled { get; set; }
    public string? SqlTargetTable { get; set; }
    public string? SqlColumnMappingJson { get; set; }
}

/// <summary>
/// Sentinel value returned by the API for secret fields that have a value stored.
/// A field whose value equals this constant has not been changed by the user.
/// </summary>
public static class ConfigurationConstants
{
    public const string MaskedSecret = "••••••••";

    /// <summary>Returns true when the value is empty or is the API-supplied mask — i.e. the user has not typed a new secret.</summary>
    public static bool IsPlaceholder(string? value) =>
        string.IsNullOrEmpty(value) || value == MaskedSecret;
}

/// <summary>Result of a connectivity test returned by POST /api/connectivity/{service}.</summary>
public class ConnectivityResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
}
