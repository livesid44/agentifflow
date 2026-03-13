using AgentifFlow.Api.Data;
using AgentifFlow.Api.Models;
using AgentifFlow.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace AgentifFlow.Tests;

public class BlobWatcherJobServiceTests
{
    private static AgentifFlowDbContext CreateDb(string name)
    {
        var options = new DbContextOptionsBuilder<AgentifFlowDbContext>()
            .UseInMemoryDatabase(name)
            .Options;
        return new AgentifFlowDbContext(options);
    }

    private static BlobWatcherJobService CreateSvc(AgentifFlowDbContext db)
    {
        var mockNotifications = new Mock<IAgentNotificationService>();
        mockNotifications.Setup(n => n.NotifyJobCreatedAsync(It.IsAny<BlobWatcherJobDto>()))
                         .Returns(Task.CompletedTask);
        mockNotifications.Setup(n => n.NotifyJobUpdatedAsync(It.IsAny<BlobWatcherJobDto>()))
                         .Returns(Task.CompletedTask);
        return new BlobWatcherJobService(db, NullLogger<BlobWatcherJobService>.Instance, mockNotifications.Object);
    }

    [Fact]
    public async Task CreateAsync_PersistsJob()
    {
        using var db = CreateDb(nameof(CreateAsync_PersistsJob));
        var svc = CreateSvc(db);

        var job = await svc.CreateAsync("data.csv", "uploads");

        Assert.True(job.Id > 0);
        Assert.Equal("data.csv", job.BlobName);
        Assert.Equal("uploads", job.ContainerName);
        Assert.Equal(BlobWatcherJobStatus.Detected, job.Status);
    }

    [Fact]
    public async Task UpdateStatusAsync_ChangesStatus()
    {
        using var db = CreateDb(nameof(UpdateStatusAsync_ChangesStatus));
        var svc = CreateSvc(db);

        var created = await svc.CreateAsync("test.csv", "container");
        var updated = await svc.UpdateStatusAsync(created.Id, BlobWatcherJobStatus.Validating, logEntry: "Started.");

        Assert.NotNull(updated);
        Assert.Equal(BlobWatcherJobStatus.Validating, updated.Status);
        Assert.Contains("Started.", updated.LogDetails);
    }

    [Fact]
    public async Task UpdateStatusAsync_SetsCompletedAt_WhenCompleted()
    {
        using var db = CreateDb(nameof(UpdateStatusAsync_SetsCompletedAt_WhenCompleted));
        var svc = CreateSvc(db);

        var created = await svc.CreateAsync("done.csv", "c");
        var updated = await svc.UpdateStatusAsync(created.Id, BlobWatcherJobStatus.Completed);

        Assert.NotNull(updated!.CompletedAt);
    }

    [Fact]
    public async Task IncrementRetryAsync_IncrementsCounter()
    {
        using var db = CreateDb(nameof(IncrementRetryAsync_IncrementsCounter));
        var svc = CreateSvc(db);

        var created = await svc.CreateAsync("retry.csv", "c");
        var updated = await svc.IncrementRetryAsync(created.Id);

        Assert.Equal(1, updated!.RetryCount);
        Assert.Equal(BlobWatcherJobStatus.Retrying, updated.Status);
    }

    [Fact]
    public async Task ExistsAsync_ReturnsTrueForActiveJob()
    {
        using var db = CreateDb(nameof(ExistsAsync_ReturnsTrueForActiveJob));
        var svc = CreateSvc(db);

        await svc.CreateAsync("active.csv", "container");
        var exists = await svc.ExistsAsync("active.csv", "container");

        Assert.True(exists);
    }

    [Fact]
    public async Task ExistsAsync_ReturnsFalseAfterRejection()
    {
        using var db = CreateDb(nameof(ExistsAsync_ReturnsFalseAfterRejection));
        var svc = CreateSvc(db);

        var created = await svc.CreateAsync("rejected.csv", "c");
        await svc.UpdateStatusAsync(created.Id, BlobWatcherJobStatus.Rejected);

        var exists = await svc.ExistsAsync("rejected.csv", "c");
        Assert.False(exists);
    }

    /// <summary>
    /// After a blob is successfully processed (Completed), the next poll cycle must
    /// NOT be blocked — ExistsAsync must return false so a new job is created and
    /// the agent triggers again on the configured frequency.
    /// </summary>
    [Fact]
    public async Task ExistsAsync_ReturnsFalseAfterCompletion()
    {
        using var db = CreateDb(nameof(ExistsAsync_ReturnsFalseAfterCompletion));
        var svc = CreateSvc(db);

        var created = await svc.CreateAsync("success.csv", "c");
        await svc.UpdateStatusAsync(created.Id, BlobWatcherJobStatus.Completed);

        var exists = await svc.ExistsAsync("success.csv", "c");

        // Completed must not block re-processing — agent triggers every poll interval.
        Assert.False(exists);
    }

    [Fact]
    public async Task GetAllAsync_ReturnsJobsOrderedByNewest()
    {
        using var db = CreateDb(nameof(GetAllAsync_ReturnsJobsOrderedByNewest));
        var svc = CreateSvc(db);

        await svc.CreateAsync("first.csv", "c");
        await Task.Delay(10);
        await svc.CreateAsync("second.csv", "c");

        var jobs = (await svc.GetAllAsync()).ToList();
        Assert.Equal(2, jobs.Count);
        Assert.Equal("second.csv", jobs[0].BlobName); // newest first
    }

    [Fact]
    public async Task SetRowsInsertedAsync_SetsValue()
    {
        using var db = CreateDb(nameof(SetRowsInsertedAsync_SetsValue));
        var svc = CreateSvc(db);

        var created = await svc.CreateAsync("rows.csv", "c");
        await svc.SetRowsInsertedAsync(created.Id, 42);

        var dto = await svc.GetByIdAsync(created.Id);
        Assert.Equal(42, dto!.RowsInserted);
    }

    [Fact]
    public async Task CreateAsync_BroadcastsJobCreatedNotification()
    {
        using var db = CreateDb(nameof(CreateAsync_BroadcastsJobCreatedNotification));
        var mockNotifications = new Mock<IAgentNotificationService>();
        mockNotifications.Setup(n => n.NotifyJobCreatedAsync(It.IsAny<BlobWatcherJobDto>()))
                         .Returns(Task.CompletedTask);
        mockNotifications.Setup(n => n.NotifyJobUpdatedAsync(It.IsAny<BlobWatcherJobDto>()))
                         .Returns(Task.CompletedTask);
        var svc = new BlobWatcherJobService(db, NullLogger<BlobWatcherJobService>.Instance, mockNotifications.Object);

        await svc.CreateAsync("notify.csv", "c");

        mockNotifications.Verify(n => n.NotifyJobCreatedAsync(It.Is<BlobWatcherJobDto>(d => d.BlobName == "notify.csv")), Times.Once);
    }

    [Fact]
    public async Task UpdateStatusAsync_BroadcastsJobUpdatedNotification()
    {
        using var db = CreateDb(nameof(UpdateStatusAsync_BroadcastsJobUpdatedNotification));
        var mockNotifications = new Mock<IAgentNotificationService>();
        mockNotifications.Setup(n => n.NotifyJobCreatedAsync(It.IsAny<BlobWatcherJobDto>()))
                         .Returns(Task.CompletedTask);
        mockNotifications.Setup(n => n.NotifyJobUpdatedAsync(It.IsAny<BlobWatcherJobDto>()))
                         .Returns(Task.CompletedTask);
        var svc = new BlobWatcherJobService(db, NullLogger<BlobWatcherJobService>.Instance, mockNotifications.Object);

        var job = await svc.CreateAsync("notify2.csv", "c");
        await svc.UpdateStatusAsync(job.Id, BlobWatcherJobStatus.Completed);

        mockNotifications.Verify(n => n.NotifyJobUpdatedAsync(It.Is<BlobWatcherJobDto>(d => d.Status == "Completed")), Times.Once);
    }

    /// <summary>
    /// Each "file not found" poll cycle must create its own BlobWatcherJob record.
    /// The sentinel blob name "[container-scan]" must never block re-creation — every
    /// cycle that finds no file should produce a new job visible in the dashboard.
    /// </summary>
    [Fact]
    public async Task CreateAsync_NoFileSentinel_CreatesMultipleJobsAcrossCycles()
    {
        const string noFileSentinel = "[container-scan]";
        const string container      = "uploads";

        using var db = CreateDb(nameof(CreateAsync_NoFileSentinel_CreatesMultipleJobsAcrossCycles));
        var svc = CreateSvc(db);

        // Simulate three consecutive poll cycles each finding no file.
        var job1 = await svc.CreateAsync(noFileSentinel, container);
        await svc.UpdateStatusAsync(job1.Id, BlobWatcherJobStatus.AwaitingApproval);

        var job2 = await svc.CreateAsync(noFileSentinel, container);
        await svc.UpdateStatusAsync(job2.Id, BlobWatcherJobStatus.AwaitingApproval);

        var job3 = await svc.CreateAsync(noFileSentinel, container);
        await svc.UpdateStatusAsync(job3.Id, BlobWatcherJobStatus.AwaitingApproval);

        // All three must have distinct IDs — one per cycle.
        Assert.True(job1.Id > 0);
        Assert.NotEqual(job1.Id, job2.Id);
        Assert.NotEqual(job2.Id, job3.Id);

        // All three must be visible in the GetAllAsync list.
        var all = (await svc.GetAllAsync(limit: 100)).ToList();
        Assert.Equal(3, all.Count(j => j.BlobName == noFileSentinel));
    }

    /// <summary>
    /// UpdateStatusAsync with a retryAfterUtc value must persist RetryAfterUtc on the job,
    /// and IncrementRetryAsync must clear it (timer resets to null on retry initiation).
    /// </summary>
    [Fact]
    public async Task UpdateStatusAsync_SetsRetryAfterUtc_AndIncrementRetryClears()
    {
        using var db  = CreateDb(nameof(UpdateStatusAsync_SetsRetryAfterUtc_AndIncrementRetryClears));
        var svc       = CreateSvc(db);

        var job        = await svc.CreateAsync("test.csv", "uploads");
        var retryAfter = DateTime.UtcNow.AddMinutes(30);

        // Set AwaitingApproval with a future retry timer.
        await svc.UpdateStatusAsync(job.Id, BlobWatcherJobStatus.AwaitingApproval,
            errorMessage: "SQL failed", retryAfterUtc: retryAfter);

        var persisted = await db.BlobWatcherJobs.FindAsync(job.Id);
        Assert.Equal(BlobWatcherJobStatus.AwaitingApproval, persisted!.Status);
        Assert.NotNull(persisted.RetryAfterUtc);
        Assert.True(Math.Abs((retryAfter - persisted.RetryAfterUtc!.Value).TotalSeconds) < 1);

        // IncrementRetryAsync must clear RetryAfterUtc.
        await svc.IncrementRetryAsync(job.Id);

        var afterRetry = await db.BlobWatcherJobs.FindAsync(job.Id);
        Assert.Equal(BlobWatcherJobStatus.Retrying, afterRetry!.Status);
        Assert.Equal(1, afterRetry.RetryCount);
        Assert.Null(afterRetry.RetryAfterUtc);
    }

    /// <summary>
    /// ExistsAsync must consider AwaitingApproval jobs as in-flight so that the same
    /// failed blob is not started as a brand-new job while waiting for auto-retry.
    /// </summary>
    [Fact]
    public async Task ExistsAsync_ReturnsTrueWhenAwaitingApproval()
    {
        using var db = CreateDb(nameof(ExistsAsync_ReturnsTrueWhenAwaitingApproval));
        var svc      = CreateSvc(db);

        var job = await svc.CreateAsync("data.csv", "container");
        await svc.UpdateStatusAsync(job.Id, BlobWatcherJobStatus.AwaitingApproval,
            retryAfterUtc: DateTime.UtcNow.AddMinutes(30));

        // The same blob must be considered in-flight.
        Assert.True(await svc.ExistsAsync("data.csv", "container"));
    }
}
