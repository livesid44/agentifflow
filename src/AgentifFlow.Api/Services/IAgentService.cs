using AgentifFlow.Api.Models;

namespace AgentifFlow.Api.Services;

public interface IAgentService
{
    Task<IEnumerable<AgentDto>> GetAllAsync();
    Task<AgentDto?> GetByIdAsync(int id);
    Task<AgentDto> CreateAsync(CreateAgentRequest request);
    Task<AgentDto?> UpdateAsync(int id, UpdateAgentRequest request);
    Task<bool> DeleteAsync(int id);

    /// <summary>Returns all skills assigned to an agent.</summary>
    Task<List<AgentSkillDto>> GetSkillsAsync(int agentId);

    /// <summary>Replaces all skills for an agent atomically.</summary>
    Task<List<AgentSkillDto>?> SetSkillsAsync(int agentId, SetSkillsRequest request);

    /// <summary>Returns the full skills catalogue (all 4 skill types).</summary>
    IEnumerable<SkillCatalogueDto> GetSkillCatalogue();

    /// <summary>Returns all enabled agents with their file targets and skills — used by the background service.</summary>
    Task<IEnumerable<Agent>> GetEnabledAgentsWithTargetsAsync();
}
