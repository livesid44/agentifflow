using AgentifFlow.Api.Models;

namespace AgentifFlow.Api.Services;

public interface IAppConfigurationService
{
    Task<AppConfigurationDto> GetConfigurationAsync();
    Task<AppConfigurationDto> UpdateConfigurationAsync(UpdateAppConfigurationRequest request, string updatedBy);

    /// <summary>
    /// Returns the raw (unmasked) Azure OpenAI settings stored in the database.
    /// Used internally by <see cref="LlmService"/> to create the Azure OpenAI client.
    /// These values are never returned to the browser.
    /// </summary>
    Task<(string? Endpoint, string? ApiKey, string? DeploymentName)> GetOpenAiRawSettingsAsync();

    /// <summary>
    /// Returns the raw (unmasked) Microsoft Graph settings stored in the database,
    /// including the mailbox address/UPN required for application-level mail access.
    /// These values are never returned to the browser.
    /// </summary>
    Task<(string? TenantId, string? ClientId, string? ClientSecret, string? MailboxAddress)> GetGraphRawSettingsAsync();

    /// <summary>Returns the raw (unmasked) Blob Storage connection string. Never sent to the browser.</summary>
    Task<string?> GetBlobRawSettingsAsync();

    /// <summary>Returns the raw (unmasked) SQL connection string. Never sent to the browser.</summary>
    Task<string?> GetSqlRawSettingsAsync();
}
