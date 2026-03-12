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
