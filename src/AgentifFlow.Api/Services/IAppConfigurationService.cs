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
}
