using AgentifFlow.BlazorWeb.Models;

namespace AgentifFlow.BlazorWeb.Services;

public interface IAgentApiService
{
    Task<List<AgentDto>> GetAllAsync();
    Task<AgentDto?> GetByIdAsync(int id);
    Task<AgentDto?> CreateAsync(CreateAgentRequest request);
    Task<AgentDto?> UpdateAsync(int id, UpdateAgentRequest request);
    Task<AgentDto?> SetEnabledAsync(int id, bool enabled);
    Task<bool> DeleteAsync(int id);

    Task<List<SkillCatalogueDto>> GetSkillCatalogueAsync();
    Task<List<AgentSkillDto>> GetSkillsAsync(int agentId);
    Task<List<AgentSkillDto>?> SetSkillsAsync(int agentId, SetSkillsRequest request);
}
