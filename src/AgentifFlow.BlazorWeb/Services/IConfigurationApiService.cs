using AgentifFlow.BlazorWeb.Models;

namespace AgentifFlow.BlazorWeb.Services;

public interface IConfigurationApiService
{
    Task<AppConfigurationDto?> GetConfigurationAsync();
    Task<AppConfigurationDto?> SaveConfigurationAsync(UpdateAppConfigurationRequest request);
    Task<ConnectivityResult?> TestConnectivityAsync(string service);
}
