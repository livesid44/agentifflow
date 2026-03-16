using System.Net.Http.Json;
using AgentifFlow.BlazorWeb.Models;

namespace AgentifFlow.BlazorWeb.Services;

public class AgentApiService : IAgentApiService
{
    private readonly HttpClient _http;

    public AgentApiService(HttpClient http) => _http = http;

    public async Task<List<AgentDto>> GetAllAsync()
        => await _http.GetFromJsonAsync<List<AgentDto>>("api/agents") ?? [];

    public async Task<AgentDto?> GetByIdAsync(int id)
        => await _http.GetFromJsonAsync<AgentDto>($"api/agents/{id}");

    public async Task<AgentDto?> CreateAsync(CreateAgentRequest request)
    {
        var response = await _http.PostAsJsonAsync("api/agents", request);
        if (!response.IsSuccessStatusCode) return null;
        return await response.Content.ReadFromJsonAsync<AgentDto>();
    }

    public async Task<AgentDto?> UpdateAsync(int id, UpdateAgentRequest request)
    {
        var response = await _http.PutAsJsonAsync($"api/agents/{id}", request);
        if (!response.IsSuccessStatusCode) return null;
        return await response.Content.ReadFromJsonAsync<AgentDto>();
    }

    public async Task<AgentDto?> SetEnabledAsync(int id, bool enabled)
    {
        var response = await _http.PatchAsJsonAsync($"api/agents/{id}/enabled", new { IsEnabled = enabled });
        if (!response.IsSuccessStatusCode) return null;
        return await response.Content.ReadFromJsonAsync<AgentDto>();
    }

    public async Task<bool> DeleteAsync(int id)
    {
        var response = await _http.DeleteAsync($"api/agents/{id}");
        return response.IsSuccessStatusCode;
    }
}
