using System.Net.Http.Json;
using AgentifFlow.BlazorWeb.Models;

namespace AgentifFlow.BlazorWeb.Services;

public class JobMonitorApiService : IJobMonitorApiService
{
    private readonly HttpClient _http;

    public JobMonitorApiService(HttpClient http) => _http = http;

    public async Task<JobMonitorStatusDto?> GetStatusAsync()
        => await _http.GetFromJsonAsync<JobMonitorStatusDto>("api/jobmonitor/status");

    public async Task<List<MonitoredJobDto>> GetJobsAsync(string? status = null)
    {
        var url = string.IsNullOrWhiteSpace(status)
            ? "api/jobmonitor"
            : $"api/jobmonitor?status={Uri.EscapeDataString(status)}";
        return await _http.GetFromJsonAsync<List<MonitoredJobDto>>(url) ?? [];
    }

    public async Task<MonitoredJobDto?> GetJobAsync(int id)
        => await _http.GetFromJsonAsync<MonitoredJobDto>($"api/jobmonitor/{id}");

    public async Task<MonitoredJobDto?> CreateJobAsync(CreateMonitoredJobRequest request)
    {
        var resp = await _http.PostAsJsonAsync("api/jobmonitor", request);
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadFromJsonAsync<MonitoredJobDto>();
    }

    public async Task<MonitoredJobDto?> SimulateFailAsync(int id)
    {
        var resp = await _http.PatchAsync($"api/jobmonitor/{id}/fail", null);
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadFromJsonAsync<MonitoredJobDto>();
    }

    public async Task<MonitoredJobDto?> UpdateJobAsync(int id, UpdateMonitoredJobRequest request)
    {
        var resp = await _http.PatchAsJsonAsync($"api/jobmonitor/{id}", request);
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadFromJsonAsync<MonitoredJobDto>();
    }

    public async Task<bool> DeleteJobAsync(int id)
    {
        var resp = await _http.DeleteAsync($"api/jobmonitor/{id}");
        return resp.IsSuccessStatusCode;
    }
}
