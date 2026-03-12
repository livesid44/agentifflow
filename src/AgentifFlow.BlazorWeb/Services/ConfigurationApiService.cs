using System.Net.Http.Json;
using AgentifFlow.BlazorWeb.Models;

namespace AgentifFlow.BlazorWeb.Services;

public class ConfigurationApiService : IConfigurationApiService
{
    private readonly HttpClient _http;

    public ConfigurationApiService(HttpClient http)
    {
        _http = http;
    }

    public async Task<AppConfigurationDto?> GetConfigurationAsync()
    {
        return await _http.GetFromJsonAsync<AppConfigurationDto>("api/configuration");
    }

    public async Task<AppConfigurationDto?> SaveConfigurationAsync(UpdateAppConfigurationRequest request)
    {
        var response = await _http.PutAsJsonAsync("api/configuration", request);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<AppConfigurationDto>();
    }

    public async Task<ConnectivityResult?> TestConnectivityAsync(string service)
    {
        var response = await _http.PostAsync($"api/connectivity/{service}", null);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<ConnectivityResult>();
    }
}
