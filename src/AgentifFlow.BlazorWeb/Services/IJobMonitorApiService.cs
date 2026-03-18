using AgentifFlow.BlazorWeb.Models;

namespace AgentifFlow.BlazorWeb.Services;

public interface IJobMonitorApiService
{
    Task<JobMonitorStatusDto?> GetStatusAsync();
    Task<List<MonitoredJobDto>> GetJobsAsync(string? status = null);
    Task<MonitoredJobDto?> GetJobAsync(int id);
    Task<MonitoredJobDto?> CreateJobAsync(CreateMonitoredJobRequest request);
    Task<MonitoredJobDto?> SimulateFailAsync(int id);
    Task<MonitoredJobDto?> UpdateJobAsync(int id, UpdateMonitoredJobRequest request);
    Task<bool> DeleteJobAsync(int id);
}
