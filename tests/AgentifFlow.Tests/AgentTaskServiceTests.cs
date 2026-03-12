using AgentifFlow.Api.Data;
using AgentifFlow.Api.Models;
using AgentifFlow.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace AgentifFlow.Tests;

public class AgentTaskServiceTests
{
    private static AgentifFlowDbContext CreateInMemoryDbContext(string dbName)
    {
        var options = new DbContextOptionsBuilder<AgentifFlowDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;
        return new AgentifFlowDbContext(options);
    }

    [Fact]
    public async Task CreateTaskAsync_ShouldPersistAndReturnTask()
    {
        using var db = CreateInMemoryDbContext(nameof(CreateTaskAsync_ShouldPersistAndReturnTask));
        var service = new AgentTaskService(db, NullLogger<AgentTaskService>.Instance);

        var task = new AgentTask { Title = "Test Task", Description = "Description" };
        var result = await service.CreateTaskAsync(task);

        Assert.True(result.Id > 0);
        Assert.Equal("Test Task", result.Title);
        Assert.Equal(AgentTaskStatus.Pending, result.Status);
        Assert.NotEqual(default, result.CreatedAt);
    }

    [Fact]
    public async Task GetTaskByIdAsync_ShouldReturnTask_WhenExists()
    {
        using var db = CreateInMemoryDbContext(nameof(GetTaskByIdAsync_ShouldReturnTask_WhenExists));
        var service = new AgentTaskService(db, NullLogger<AgentTaskService>.Instance);

        var created = await service.CreateTaskAsync(new AgentTask { Title = "Find Me" });
        var found = await service.GetTaskByIdAsync(created.Id);

        Assert.NotNull(found);
        Assert.Equal("Find Me", found.Title);
    }

    [Fact]
    public async Task GetTaskByIdAsync_ShouldReturnNull_WhenNotExists()
    {
        using var db = CreateInMemoryDbContext(nameof(GetTaskByIdAsync_ShouldReturnNull_WhenNotExists));
        var service = new AgentTaskService(db, NullLogger<AgentTaskService>.Instance);

        var result = await service.GetTaskByIdAsync(999);

        Assert.Null(result);
    }

    [Fact]
    public async Task GetAllTasksAsync_ShouldReturnAllTasks()
    {
        using var db = CreateInMemoryDbContext(nameof(GetAllTasksAsync_ShouldReturnAllTasks));
        var service = new AgentTaskService(db, NullLogger<AgentTaskService>.Instance);

        await service.CreateTaskAsync(new AgentTask { Title = "Task 1" });
        await service.CreateTaskAsync(new AgentTask { Title = "Task 2" });
        await service.CreateTaskAsync(new AgentTask { Title = "Task 3" });

        var tasks = await service.GetAllTasksAsync();

        Assert.Equal(3, tasks.Count());
    }

    [Fact]
    public async Task UpdateTaskAsync_ShouldUpdateTitle_WhenExists()
    {
        using var db = CreateInMemoryDbContext(nameof(UpdateTaskAsync_ShouldUpdateTitle_WhenExists));
        var service = new AgentTaskService(db, NullLogger<AgentTaskService>.Instance);

        var created = await service.CreateTaskAsync(new AgentTask { Title = "Original" });
        var update = new AgentTask { Title = "Updated", Status = AgentTaskStatus.InProgress };

        var result = await service.UpdateTaskAsync(created.Id, update);

        Assert.NotNull(result);
        Assert.Equal("Updated", result.Title);
        Assert.Equal(AgentTaskStatus.InProgress, result.Status);
        Assert.NotNull(result.UpdatedAt);
    }

    [Fact]
    public async Task UpdateTaskAsync_ShouldSetCompletedAt_WhenStatusBecomesCompleted()
    {
        using var db = CreateInMemoryDbContext(nameof(UpdateTaskAsync_ShouldSetCompletedAt_WhenStatusBecomesCompleted));
        var service = new AgentTaskService(db, NullLogger<AgentTaskService>.Instance);

        var created = await service.CreateTaskAsync(new AgentTask { Title = "Complete Me" });
        var update = new AgentTask { Title = "Complete Me", Status = AgentTaskStatus.Completed };

        var result = await service.UpdateTaskAsync(created.Id, update);

        Assert.NotNull(result);
        Assert.Equal(AgentTaskStatus.Completed, result.Status);
        Assert.NotNull(result.CompletedAt);
    }

    [Fact]
    public async Task UpdateTaskAsync_ShouldReturnNull_WhenTaskNotExists()
    {
        using var db = CreateInMemoryDbContext(nameof(UpdateTaskAsync_ShouldReturnNull_WhenTaskNotExists));
        var service = new AgentTaskService(db, NullLogger<AgentTaskService>.Instance);

        var result = await service.UpdateTaskAsync(999, new AgentTask { Title = "Ghost" });

        Assert.Null(result);
    }

    [Fact]
    public async Task DeleteTaskAsync_ShouldReturnTrue_WhenExists()
    {
        using var db = CreateInMemoryDbContext(nameof(DeleteTaskAsync_ShouldReturnTrue_WhenExists));
        var service = new AgentTaskService(db, NullLogger<AgentTaskService>.Instance);

        var created = await service.CreateTaskAsync(new AgentTask { Title = "Delete Me" });
        var deleted = await service.DeleteTaskAsync(created.Id);
        var found = await service.GetTaskByIdAsync(created.Id);

        Assert.True(deleted);
        Assert.Null(found);
    }

    [Fact]
    public async Task DeleteTaskAsync_ShouldReturnFalse_WhenNotExists()
    {
        using var db = CreateInMemoryDbContext(nameof(DeleteTaskAsync_ShouldReturnFalse_WhenNotExists));
        var service = new AgentTaskService(db, NullLogger<AgentTaskService>.Instance);

        var result = await service.DeleteTaskAsync(999);

        Assert.False(result);
    }

    [Fact]
    public async Task GetTasksByStatusAsync_ShouldReturnFilteredTasks()
    {
        using var db = CreateInMemoryDbContext(nameof(GetTasksByStatusAsync_ShouldReturnFilteredTasks));
        var service = new AgentTaskService(db, NullLogger<AgentTaskService>.Instance);

        await service.CreateTaskAsync(new AgentTask { Title = "Pending 1" });
        await service.CreateTaskAsync(new AgentTask { Title = "Pending 2" });
        var inProgress = await service.CreateTaskAsync(new AgentTask { Title = "In Progress" });
        await service.UpdateTaskAsync(inProgress.Id, new AgentTask { Title = "In Progress", Status = AgentTaskStatus.InProgress });

        var pending = await service.GetTasksByStatusAsync(AgentTaskStatus.Pending);
        var inProgressTasks = await service.GetTasksByStatusAsync(AgentTaskStatus.InProgress);

        Assert.Equal(2, pending.Count());
        Assert.Single(inProgressTasks);
    }
}
