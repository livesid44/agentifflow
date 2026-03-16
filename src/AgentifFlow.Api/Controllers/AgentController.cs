using AgentifFlow.Api.Models;
using AgentifFlow.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Identity.Web.Resource;

namespace AgentifFlow.Api.Controllers;

[ApiController]
[Route("api/agents")]
[Authorize]
[RequiredScope(RequiredScopesConfigurationKey = "AzureAd:Scopes")]
public class AgentController : ControllerBase
{
    private readonly IAgentService _agentService;

    public AgentController(IAgentService agentService)
    {
        _agentService = agentService;
    }

    /// <summary>Returns all agents with their file targets and job statistics.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(IEnumerable<AgentDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAll()
    {
        var agents = await _agentService.GetAllAsync();
        return Ok(agents);
    }

    /// <summary>Returns a single agent by ID.</summary>
    [HttpGet("{id:int}")]
    [ProducesResponseType(typeof(AgentDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(int id)
    {
        var agent = await _agentService.GetByIdAsync(id);
        if (agent is null) return NotFound();
        return Ok(agent);
    }

    /// <summary>Creates a new agent.</summary>
    [HttpPost]
    [ProducesResponseType(typeof(AgentDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Create([FromBody] CreateAgentRequest request)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);
        var agent = await _agentService.CreateAsync(request);
        return CreatedAtAction(nameof(GetById), new { id = agent.Id }, agent);
    }

    /// <summary>Updates an existing agent.  Only supplied fields are overwritten.</summary>
    [HttpPut("{id:int}")]
    [ProducesResponseType(typeof(AgentDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Update(int id, [FromBody] UpdateAgentRequest request)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);
        var agent = await _agentService.UpdateAsync(id, request);
        if (agent is null) return NotFound();
        return Ok(agent);
    }

    /// <summary>Enables or disables an agent.</summary>
    [HttpPatch("{id:int}/enabled")]
    [ProducesResponseType(typeof(AgentDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> SetEnabled(int id, [FromBody] SetEnabledRequest request)
    {
        var agent = await _agentService.UpdateAsync(id, new UpdateAgentRequest { IsEnabled = request.IsEnabled });
        if (agent is null) return NotFound();
        return Ok(agent);
    }

    /// <summary>Deletes an agent and all associated file targets.</summary>
    [HttpDelete("{id:int}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(int id)
    {
        var deleted = await _agentService.DeleteAsync(id);
        if (!deleted) return NotFound();
        return NoContent();
    }
}

public record SetEnabledRequest(bool IsEnabled);
