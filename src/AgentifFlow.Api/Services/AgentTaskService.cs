using AgentifFlow.Api.Data;
using AgentifFlow.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace AgentifFlow.Api.Services;

public class AgentTaskService : IAgentTaskService
{
    private readonly AgentifFlowDbContext _dbContext;
    private readonly ILogger<AgentTaskService> _logger;

    public AgentTaskService(AgentifFlowDbContext dbContext, ILogger<AgentTaskService> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task<IEnumerable<AgentTask>> GetAllTasksAsync()
    {
        _logger.LogInformation("Retrieving all agent tasks");
        return await _dbContext.AgentTasks
            .OrderByDescending(t => t.CreatedAt)
            .ToListAsync();
    }

    public async Task<AgentTask?> GetTaskByIdAsync(int id)
    {
        _logger.LogInformation("Retrieving agent task {Id}", id);
        return await _dbContext.AgentTasks.FindAsync(id);
    }

    public async Task<AgentTask> CreateTaskAsync(AgentTask task)
    {
        _logger.LogInformation("Creating agent task: {Title}", task.Title);
        task.CreatedAt = DateTime.UtcNow;
        task.UpdatedAt = null;
        task.CompletedAt = null;

        _dbContext.AgentTasks.Add(task);
        await _dbContext.SaveChangesAsync();
        return task;
    }

    public async Task<AgentTask?> UpdateTaskAsync(int id, AgentTask updatedTask)
    {
        _logger.LogInformation("Updating agent task {Id}", id);
        var existing = await _dbContext.AgentTasks.FindAsync(id);
        if (existing is null) return null;

        existing.Title = updatedTask.Title;
        existing.Description = updatedTask.Description;
        existing.AssignedTo = updatedTask.AssignedTo;
        existing.Result = updatedTask.Result;
        existing.UpdatedAt = DateTime.UtcNow;

        if (updatedTask.Status != existing.Status)
        {
            existing.Status = updatedTask.Status;
            if (updatedTask.Status == AgentTaskStatus.Completed)
            {
                existing.CompletedAt = DateTime.UtcNow;
            }
        }

        await _dbContext.SaveChangesAsync();
        return existing;
    }

    public async Task<bool> DeleteTaskAsync(int id)
    {
        _logger.LogInformation("Deleting agent task {Id}", id);
        var task = await _dbContext.AgentTasks.FindAsync(id);
        if (task is null) return false;

        _dbContext.AgentTasks.Remove(task);
        await _dbContext.SaveChangesAsync();
        return true;
    }

    public async Task<IEnumerable<AgentTask>> GetTasksByStatusAsync(AgentTaskStatus status)
    {
        _logger.LogInformation("Retrieving agent tasks with status {Status}", status);
        return await _dbContext.AgentTasks
            .Where(t => t.Status == status)
            .OrderByDescending(t => t.CreatedAt)
            .ToListAsync();
    }
}
