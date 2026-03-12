using AgentifFlow.BlazorWeb.Models;

namespace AgentifFlow.BlazorWeb.Services;

public interface IBlobJobApiService
{
    Task<List<BlobWatcherJobDto>> GetJobsAsync(int limit = 50);
    Task<BlobWatcherJobDto?> GetJobAsync(int id);
    Task<BlobWatcherJobDto?> ApproveJobAsync(int id);
    Task<BlobWatcherJobDto?> RejectJobAsync(int id);
}
