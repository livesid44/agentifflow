using AgentifFlow.Api.Data;
using AgentifFlow.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace AgentifFlow.Api.Services;

public class BlobWatcherJobService : IBlobWatcherJobService
{
    private readonly AgentifFlowDbContext _db;
    private readonly ILogger<BlobWatcherJobService> _logger;
    private readonly IAgentNotificationService _notifications;

    public BlobWatcherJobService(
        AgentifFlowDbContext db,
        ILogger<BlobWatcherJobService> logger,
        IAgentNotificationService notifications)
    {
        _db            = db;
        _logger        = logger;
        _notifications = notifications;
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

    public async Task<BlobWatcherJob> CreateAsync(string blobName, string? containerName, int? agentId = null)
    {
        var job = new BlobWatcherJob
        {
            BlobName      = blobName,
            ContainerName = containerName,
            AgentId       = agentId,
            Status        = BlobWatcherJobStatus.Detected,
            DetectedAt    = DateTime.UtcNow,
            UpdatedAt     = DateTime.UtcNow
        };
        _db.BlobWatcherJobs.Add(job);
        await _db.SaveChangesAsync();
        _logger.LogInformation("BlobWatcherJob created for blob '{BlobName}'", blobName);
        await _notifications.NotifyJobCreatedAsync(ToDto(job));
        return job;
    }

    public async Task<BlobWatcherJob?> UpdateStatusAsync(int id, BlobWatcherJobStatus status,
        string? errorMessage = null, string? logEntry = null, DateTime? retryAfterUtc = null)
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

        if (retryAfterUtc.HasValue)
            job.RetryAfterUtc = retryAfterUtc.Value;

        if (status is BlobWatcherJobStatus.Completed or BlobWatcherJobStatus.Failed or BlobWatcherJobStatus.Rejected)
            job.CompletedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync();
        await _notifications.NotifyJobUpdatedAsync(ToDto(job));
        return job;
    }

    public async Task<BlobWatcherJob?> IncrementRetryAsync(int id)
    {
        var job = await _db.BlobWatcherJobs.FindAsync(id);
        if (job is null) return null;

        job.RetryCount++;
        job.Status        = BlobWatcherJobStatus.Retrying;
        job.UpdatedAt     = DateTime.UtcNow;
        job.RetryAfterUtc = null;   // cleared — timer resets on next failure
        job.LogDetails = string.IsNullOrEmpty(job.LogDetails)
            ? $"[{DateTime.UtcNow:u}] Retry {job.RetryCount} initiated."
            : $"{job.LogDetails}\n[{DateTime.UtcNow:u}] Retry {job.RetryCount} initiated.";

        await _db.SaveChangesAsync();
        await _notifications.NotifyJobUpdatedAsync(ToDto(job));
        return job;
    }

    public async Task<BlobWatcherJob?> SetRowsInsertedAsync(int id, int rows)
    {
        var job = await _db.BlobWatcherJobs.FindAsync(id);
        if (job is null) return null;

        job.RowsInserted = rows;
        job.UpdatedAt    = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        await _notifications.NotifyJobUpdatedAsync(ToDto(job));
        return job;
    }

    public async Task<bool> ExistsAsync(string blobName, string? containerName)
    {
        // Only block re-processing when a job for this blob is actively in-flight or
        // awaiting human action.  A Completed job must NOT block re-processing so that
        // the agent continues to trigger on every configured poll interval regardless of
        // whether the previous run was successful.
        return await _db.BlobWatcherJobs.AnyAsync(j =>
            j.BlobName == blobName && j.ContainerName == containerName &&
            (j.Status == BlobWatcherJobStatus.Detected              ||
             j.Status == BlobWatcherJobStatus.Validating             ||
             j.Status == BlobWatcherJobStatus.ValidationFailed       ||
             j.Status == BlobWatcherJobStatus.AwaitingApproval       ||
             j.Status == BlobWatcherJobStatus.Inserting              ||
             j.Status == BlobWatcherJobStatus.Retrying               ||
             j.Status == BlobWatcherJobStatus.ReplyReceived          ||
             j.Status == BlobWatcherJobStatus.AwaitingLogConfirmation ||
             j.Status == BlobWatcherJobStatus.AwaitingPocApproval));
    }

    public async Task<BlobWatcherJob?> SetNotificationRefAsync(int id, string notificationRef)
    {
        var job = await _db.BlobWatcherJobs.FindAsync(id);
        if (job is null) return null;

        job.NotificationRef = notificationRef;
        job.UpdatedAt       = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        await _notifications.NotifyJobUpdatedAsync(ToDto(job));
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
        await _notifications.NotifyJobUpdatedAsync(ToDto(job));
        return job;
    }

    private static BlobWatcherJobDto ToDto(BlobWatcherJob j) => new()
    {
        Id              = j.Id,
        AgentId         = j.AgentId,
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
        RetryAfterUtc   = j.RetryAfterUtc,
        NotificationRef = j.NotificationRef,
        UserReply       = j.UserReply
    };
}
