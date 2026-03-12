using System.Net.Http.Json;
using AgentifFlow.BlazorWeb.Models;

namespace AgentifFlow.BlazorWeb.Services;

public class BlobJobApiService : IBlobJobApiService
{
    private readonly HttpClient _http;

    public BlobJobApiService(HttpClient http) => _http = http;

    public async Task<List<BlobWatcherJobDto>> GetJobsAsync(int limit = 50)
    {
        return await _http.GetFromJsonAsync<List<BlobWatcherJobDto>>(
            $"api/blob-jobs?limit={limit}") ?? [];
    }

    public async Task<BlobWatcherJobDto?> GetJobAsync(int id)
    {
        return await _http.GetFromJsonAsync<BlobWatcherJobDto>($"api/blob-jobs/{id}");
    }

    public async Task<BlobWatcherJobDto?> ApproveJobAsync(int id)
    {
        var response = await _http.PostAsync($"api/blob-jobs/{id}/approve", null);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<BlobWatcherJobDto>();
    }

    public async Task<BlobWatcherJobDto?> RejectJobAsync(int id)
    {
        var response = await _http.PostAsync($"api/blob-jobs/{id}/reject", null);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<BlobWatcherJobDto>();
    }
}
