using AgentifFlow.Api.Models;
using AgentifFlow.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Identity.Web.Resource;

namespace AgentifFlow.Api.Controllers;

[ApiController]
[Route("api/blob-jobs")]
[Authorize]
[RequiredScope(RequiredScopesConfigurationKey = "AzureAd:Scopes")]
public class BlobJobController : ControllerBase
{
    private readonly IBlobWatcherJobService _jobService;
    private readonly ILogger<BlobJobController> _logger;

    public BlobJobController(IBlobWatcherJobService jobService, ILogger<BlobJobController> logger)
    {
        _jobService = jobService;
        _logger = logger;
    }

    /// <summary>Returns the latest blob watcher jobs (most recent first).</summary>
    [HttpGet]
    [ProducesResponseType(typeof(IEnumerable<BlobWatcherJobDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAll([FromQuery] int limit = 50)
    {
        var jobs = await _jobService.GetAllAsync(limit);
        return Ok(jobs);
    }

    /// <summary>Returns a single blob watcher job by ID.</summary>
    [HttpGet("{id:int}")]
    [ProducesResponseType(typeof(BlobWatcherJobDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(int id)
    {
        var job = await _jobService.GetByIdAsync(id);
        if (job is null) return NotFound();
        return Ok(job);
    }

    /// <summary>Manually triggers a blob-watching scan (creates a test job entry).</summary>
    [HttpPost("trigger")]
    [ProducesResponseType(typeof(BlobWatcherJobDto), StatusCodes.Status201Created)]
    public async Task<IActionResult> Trigger([FromBody] TriggerJobRequest request)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);

        var job = await _jobService.CreateAsync(request.BlobName, request.ContainerName);
        var dto = await _jobService.GetByIdAsync(job.Id);
        return CreatedAtAction(nameof(GetById), new { id = job.Id }, dto);
    }

    /// <summary>Marks a job as approved so it can be retried.</summary>
    [HttpPost("{id:int}/approve")]
    [ProducesResponseType(typeof(BlobWatcherJobDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Approve(int id)
    {
        var job = await _jobService.UpdateStatusAsync(id, BlobWatcherJobStatus.Retrying,
            logEntry: "Manually approved for retry.");
        if (job is null) return NotFound();
        return Ok(await _jobService.GetByIdAsync(id));
    }

    /// <summary>Rejects a failed job (no more retries).</summary>
    [HttpPost("{id:int}/reject")]
    [ProducesResponseType(typeof(BlobWatcherJobDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Reject(int id)
    {
        var job = await _jobService.UpdateStatusAsync(id, BlobWatcherJobStatus.Rejected,
            logEntry: "Manually rejected.");
        if (job is null) return NotFound();
        return Ok(await _jobService.GetByIdAsync(id));
    }
}

public record TriggerJobRequest(
    [property: System.ComponentModel.DataAnnotations.Required]
    [property: System.ComponentModel.DataAnnotations.MaxLength(500)]
    string BlobName,
    [property: System.ComponentModel.DataAnnotations.MaxLength(200)]
    string? ContainerName = null
);
