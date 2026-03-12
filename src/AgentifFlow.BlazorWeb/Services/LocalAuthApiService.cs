using System.Net.Http.Json;
using AgentifFlow.BlazorWeb.Models;

namespace AgentifFlow.BlazorWeb.Services;

public class LocalAuthApiService : ILocalAuthApiService
{
    private readonly HttpClient _http;

    public LocalAuthApiService(HttpClient http) => _http = http;

    public async Task<LocalLoginResponse?> LoginAsync(string username, string password)
    {
        var response = await _http.PostAsJsonAsync(
            "api/auth/local-login",
            new LocalLoginRequest(username, password));

        return response.IsSuccessStatusCode
            ? await response.Content.ReadFromJsonAsync<LocalLoginResponse>()
            : null;
    }
}
