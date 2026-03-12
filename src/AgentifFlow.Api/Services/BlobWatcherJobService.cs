using AgentifFlow.Api.Data;
using AgentifFlow.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace AgentifFlow.Api.Services;

public class BlobWatcherJobService : IBlobWatcherJobService
{
    private readonly AgentifFlowDbContext _db;
    private readonly ILogger<BlobWatcherJobService> _logger;

    public BlobWatcherJobService(AgentifFlowDbContext db, ILogger<BlobWatcherJobService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<IEnumerable<BlobWatcherJobDto>> GetAllAsync(int? limit = 50)
    {
        var query = _db.BlobWatcherJobs
            .OrderByDescending(j => j.DetectedAt)
            .AsQueryable();

        if (limit.HasValue)
            query = query.Take(limit.Value);

        return await query.Select(j => ToDto(j)).ToListAsync();
    }

    public async Task<BlobWatcherJobDto?> GetByIdAsync(int id)
    {
        var job = await _db.BlobWatcherJobs.FindAsync(id);
        return job is null ? null : ToDto(job);
    }

    public async Task<BlobWatcherJob> CreateAsync(string blobName, string? containerName)
    {
        var job = new BlobWatcherJob
        {
            BlobName      = blobName,
            ContainerName = containerName,
            Status        = BlobWatcherJobStatus.Detected,
            DetectedAt    = DateTime.UtcNow,
            UpdatedAt     = DateTime.UtcNow
        };
        _db.BlobWatcherJobs.Add(job);
        await _db.SaveChangesAsync();
        _logger.LogInformation("BlobWatcherJob created for blob '{BlobName}'", blobName);
        return job;
    }

    public async Task<BlobWatcherJob?> UpdateStatusAsync(int id, BlobWatcherJobStatus status,
        string? errorMessage = null, string? logEntry = null)
    {
        var job = await _db.BlobWatcherJobs.FindAsync(id);
        if (job is null) return null;

        job.Status    = status;
        job.UpdatedAt = DateTime.UtcNow;

        if (errorMessage is not null)
            job.ErrorMessage = errorMessage;

        if (logEntry is not null)
            job.LogDetails = string.IsNullOrEmpty(job.LogDetails)
                ? $"[{DateTime.UtcNow:u}] {logEntry}"
                : $"{job.LogDetails}\n[{DateTime.UtcNow:u}] {logEntry}";

        if (status is BlobWatcherJobStatus.Completed or BlobWatcherJobStatus.Failed or BlobWatcherJobStatus.Rejected)
            job.CompletedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync();
        return job;
    }

    public async Task<BlobWatcherJob?> IncrementRetryAsync(int id)
    {
        var job = await _db.BlobWatcherJobs.FindAsync(id);
        if (job is null) return null;

        job.RetryCount++;
        job.Status    = BlobWatcherJobStatus.Retrying;
        job.UpdatedAt = DateTime.UtcNow;
        job.LogDetails = string.IsNullOrEmpty(job.LogDetails)
            ? $"[{DateTime.UtcNow:u}] Retry {job.RetryCount} initiated."
            : $"{job.LogDetails}\n[{DateTime.UtcNow:u}] Retry {job.RetryCount} initiated.";

        await _db.SaveChangesAsync();
        return job;
    }

    public async Task<BlobWatcherJob?> SetRowsInsertedAsync(int id, int rows)
    {
        var job = await _db.BlobWatcherJobs.FindAsync(id);
        if (job is null) return null;

        job.RowsInserted = rows;
        job.UpdatedAt    = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return job;
    }

    public async Task<bool> ExistsAsync(string blobName, string? containerName)
    {
        return await _db.BlobWatcherJobs.AnyAsync(j =>
            j.BlobName == blobName && j.ContainerName == containerName &&
            j.Status != BlobWatcherJobStatus.Failed &&
            j.Status != BlobWatcherJobStatus.Rejected);
    }

    public async Task<BlobWatcherJob?> SetNotificationRefAsync(int id, string notificationRef)
    {
        var job = await _db.BlobWatcherJobs.FindAsync(id);
        if (job is null) return null;

        job.NotificationRef = notificationRef;
        job.UpdatedAt       = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return job;
    }

    public async Task<BlobWatcherJob?> SetUserReplyAsync(int id, string fromAddress, string replyPreview)
    {
        var job = await _db.BlobWatcherJobs.FindAsync(id);
        if (job is null) return null;

        job.UserReply = $"From: {fromAddress}\n{replyPreview}";
        job.Status    = BlobWatcherJobStatus.ReplyReceived;
        job.UpdatedAt = DateTime.UtcNow;
        job.LogDetails = string.IsNullOrEmpty(job.LogDetails)
            ? $"[{DateTime.UtcNow:u}] Reply received from {fromAddress}: {replyPreview[..Math.Min(200, replyPreview.Length)]}"
            : $"{job.LogDetails}\n[{DateTime.UtcNow:u}] Reply received from {fromAddress}: {replyPreview[..Math.Min(200, replyPreview.Length)]}";

        await _db.SaveChangesAsync();
        return job;
    }

    private static BlobWatcherJobDto ToDto(BlobWatcherJob j) => new()
    {
        Id              = j.Id,
        BlobName        = j.BlobName,
        ContainerName   = j.ContainerName,
        Status          = j.Status.ToString(),
        RetryCount      = j.RetryCount,
        ErrorMessage    = j.ErrorMessage,
        LogDetails      = j.LogDetails,
        RowsInserted    = j.RowsInserted,
        DetectedAt      = j.DetectedAt,
        CompletedAt     = j.CompletedAt,
        UpdatedAt       = j.UpdatedAt,
        NotificationRef = j.NotificationRef,
        UserReply       = j.UserReply
    };
}
