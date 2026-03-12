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

    public async Task<ConnectivityResult?> TestMailConnectivityAsync(string toEmail)
    {
        // When the caller provides no address, fall back to the parameterless endpoint so
        // the API can apply its own fallback to the saved NotificationEmail.
        string url;
        if (string.IsNullOrWhiteSpace(toEmail))
        {
            url = "api/connectivity/mail";
        }
        else
        {
            url = $"api/connectivity/mail?to={Uri.EscapeDataString(toEmail.Trim())}";
        }

        var response = await _http.PostAsync(url, null);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<ConnectivityResult>();
    }
}
