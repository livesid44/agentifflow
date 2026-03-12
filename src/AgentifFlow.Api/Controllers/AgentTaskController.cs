using AgentifFlow.Api.Models;
using AgentifFlow.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Identity.Web.Resource;

namespace AgentifFlow.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
[RequiredScope(RequiredScopesConfigurationKey = "AzureAd:Scopes")]
public class AgentTaskController : ControllerBase
{
    private readonly IAgentTaskService _taskService;
    private readonly ILogger<AgentTaskController> _logger;

    public AgentTaskController(IAgentTaskService taskService, ILogger<AgentTaskController> logger)
    {
        _taskService = taskService;
        _logger = logger;
    }

    /// <summary>Gets all agent tasks.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(IEnumerable<AgentTask>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAll()
    {
        var tasks = await _taskService.GetAllTasksAsync();
        return Ok(tasks);
    }

    /// <summary>Gets agent tasks filtered by status.</summary>
    [HttpGet("status/{status}")]
    [ProducesResponseType(typeof(IEnumerable<AgentTask>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetByStatus(AgentTaskStatus status)
    {
        var tasks = await _taskService.GetTasksByStatusAsync(status);
        return Ok(tasks);
    }

    /// <summary>Gets a specific agent task by ID.</summary>
    [HttpGet("{id:int}")]
    [ProducesResponseType(typeof(AgentTask), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(int id)
    {
        var task = await _taskService.GetTaskByIdAsync(id);
        if (task is null) return NotFound();
        return Ok(task);
    }

    /// <summary>Creates a new agent task.</summary>
    [HttpPost]
    [ProducesResponseType(typeof(AgentTask), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Create([FromBody] AgentTask task)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);

        var created = await _taskService.CreateTaskAsync(task);
        return CreatedAtAction(nameof(GetById), new { id = created.Id }, created);
    }

    /// <summary>Updates an existing agent task.</summary>
    [HttpPut("{id:int}")]
    [ProducesResponseType(typeof(AgentTask), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Update(int id, [FromBody] AgentTask task)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);

        var updated = await _taskService.UpdateTaskAsync(id, task);
        if (updated is null) return NotFound();
        return Ok(updated);
    }

    /// <summary>Deletes an agent task.</summary>
    [HttpDelete("{id:int}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(int id)
    {
        var deleted = await _taskService.DeleteTaskAsync(id);
        if (!deleted) return NotFound();
        return NoContent();
    }
}
