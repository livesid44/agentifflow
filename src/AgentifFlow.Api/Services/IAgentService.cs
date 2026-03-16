using AgentifFlow.Api.Models;

namespace AgentifFlow.Api.Services;

public interface IAgentService
{
    Task<IEnumerable<AgentDto>> GetAllAsync();
    Task<AgentDto?> GetByIdAsync(int id);
    Task<AgentDto> CreateAsync(CreateAgentRequest request);
    Task<AgentDto?> UpdateAsync(int id, UpdateAgentRequest request);
    Task<bool> DeleteAsync(int id);
    /// <summary>Returns all enabled agents with their file targets — used by the background service.</summary>
    Task<IEnumerable<Agent>> GetEnabledAgentsWithTargetsAsync();
}
