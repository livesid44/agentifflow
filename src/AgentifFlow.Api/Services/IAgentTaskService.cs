using AgentifFlow.Api.Models;

namespace AgentifFlow.Api.Services;

public interface IAgentTaskService
{
    Task<IEnumerable<AgentTask>> GetAllTasksAsync();
    Task<AgentTask?> GetTaskByIdAsync(int id);
    Task<AgentTask> CreateTaskAsync(AgentTask task);
    Task<AgentTask?> UpdateTaskAsync(int id, AgentTask task);
    Task<bool> DeleteTaskAsync(int id);
    Task<IEnumerable<AgentTask>> GetTasksByStatusAsync(AgentTaskStatus status);
}
