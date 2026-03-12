using AgentifFlow.Api.Data;
using AgentifFlow.Api.Models;
using AgentifFlow.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

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

    [Fact]
    public async Task CreateAsync_PersistsJob()
    {
        using var db = CreateDb(nameof(CreateAsync_PersistsJob));
        var svc = new BlobWatcherJobService(db, NullLogger<BlobWatcherJobService>.Instance);

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
        var svc = new BlobWatcherJobService(db, NullLogger<BlobWatcherJobService>.Instance);

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
        var svc = new BlobWatcherJobService(db, NullLogger<BlobWatcherJobService>.Instance);

        var created = await svc.CreateAsync("done.csv", "c");
        var updated = await svc.UpdateStatusAsync(created.Id, BlobWatcherJobStatus.Completed);

        Assert.NotNull(updated!.CompletedAt);
    }

    [Fact]
    public async Task IncrementRetryAsync_IncrementsCounter()
    {
        using var db = CreateDb(nameof(IncrementRetryAsync_IncrementsCounter));
        var svc = new BlobWatcherJobService(db, NullLogger<BlobWatcherJobService>.Instance);

        var created = await svc.CreateAsync("retry.csv", "c");
        var updated = await svc.IncrementRetryAsync(created.Id);

        Assert.Equal(1, updated!.RetryCount);
        Assert.Equal(BlobWatcherJobStatus.Retrying, updated.Status);
    }

    [Fact]
    public async Task ExistsAsync_ReturnsTrueForActiveJob()
    {
        using var db = CreateDb(nameof(ExistsAsync_ReturnsTrueForActiveJob));
        var svc = new BlobWatcherJobService(db, NullLogger<BlobWatcherJobService>.Instance);

        await svc.CreateAsync("active.csv", "container");
        var exists = await svc.ExistsAsync("active.csv", "container");

        Assert.True(exists);
    }

    [Fact]
    public async Task ExistsAsync_ReturnsFalseAfterRejection()
    {
        using var db = CreateDb(nameof(ExistsAsync_ReturnsFalseAfterRejection));
        var svc = new BlobWatcherJobService(db, NullLogger<BlobWatcherJobService>.Instance);

        var created = await svc.CreateAsync("rejected.csv", "c");
        await svc.UpdateStatusAsync(created.Id, BlobWatcherJobStatus.Rejected);

        var exists = await svc.ExistsAsync("rejected.csv", "c");
        Assert.False(exists);
    }

    [Fact]
    public async Task GetAllAsync_ReturnsJobsOrderedByNewest()
    {
        using var db = CreateDb(nameof(GetAllAsync_ReturnsJobsOrderedByNewest));
        var svc = new BlobWatcherJobService(db, NullLogger<BlobWatcherJobService>.Instance);

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
        var svc = new BlobWatcherJobService(db, NullLogger<BlobWatcherJobService>.Instance);

        var created = await svc.CreateAsync("rows.csv", "c");
        await svc.SetRowsInsertedAsync(created.Id, 42);

        var dto = await svc.GetByIdAsync(created.Id);
        Assert.Equal(42, dto!.RowsInserted);
    }
}
