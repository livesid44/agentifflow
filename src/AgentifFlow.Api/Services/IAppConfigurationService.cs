using AgentifFlow.Api.Models;

namespace AgentifFlow.Api.Services;

public interface IAppConfigurationService
{
    Task<AppConfigurationDto> GetConfigurationAsync();
    Task<AppConfigurationDto> UpdateConfigurationAsync(UpdateAppConfigurationRequest request, string updatedBy);
}
