using AgentifFlow.Api.Models;

namespace AgentifFlow.Api.Services;

public interface IBlobWatcherJobService
{
    Task<IEnumerable<BlobWatcherJobDto>> GetAllAsync(int? limit = 50);
    Task<BlobWatcherJobDto?> GetByIdAsync(int id);
    Task<BlobWatcherJob> CreateAsync(string blobName, string? containerName);
    Task<BlobWatcherJob?> UpdateStatusAsync(int id, BlobWatcherJobStatus status, string? errorMessage = null, string? logEntry = null);
    Task<BlobWatcherJob?> IncrementRetryAsync(int id);
    Task<BlobWatcherJob?> SetRowsInsertedAsync(int id, int rows);
    Task<bool> ExistsAsync(string blobName, string? containerName);
}
