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
public class LlmController : ControllerBase
{
    private readonly ILlmService _llmService;
    private readonly ILogger<LlmController> _logger;

    public LlmController(ILlmService llmService, ILogger<LlmController> logger)
    {
        _llmService = llmService;
        _logger = logger;
    }

    /// <summary>Sends a chat message to the LLM and returns a completion.</summary>
    [HttpPost("chat")]
    [ProducesResponseType(typeof(ChatResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Chat([FromBody] ChatRequest request)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);

        var response = await _llmService.ChatAsync(request);
        return Ok(response);
    }

    /// <summary>Summarizes the provided text using the LLM.</summary>
    [HttpPost("summarize")]
    [ProducesResponseType(typeof(string), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Summarize([FromBody] SummarizeRequest request)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);

        var summary = await _llmService.SummarizeAsync(request.Text);
        return Ok(new { summary });
    }

    /// <summary>Extracts actionable tasks from the provided text using the LLM.</summary>
    [HttpPost("extract-tasks")]
    [ProducesResponseType(typeof(string), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ExtractTasks([FromBody] SummarizeRequest request)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);

        var tasks = await _llmService.ExtractTasksAsync(request.Text);
        return Ok(new { tasks });
    }
}

public record SummarizeRequest(
    [property: System.ComponentModel.DataAnnotations.Required]
    [property: System.ComponentModel.DataAnnotations.MaxLength(16000)]
    string Text
);
