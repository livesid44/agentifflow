using AgentifFlow.Api.Models;
using AgentifFlow.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Identity.Web.Resource;
using System.Security.Claims;

namespace AgentifFlow.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
[RequiredScope(RequiredScopesConfigurationKey = "AzureAd:Scopes")]
public class ConfigurationController : ControllerBase
{
    private readonly IAppConfigurationService _configService;
    private readonly ILogger<ConfigurationController> _logger;

    public ConfigurationController(IAppConfigurationService configService, ILogger<ConfigurationController> logger)
    {
        _configService = configService;
        _logger = logger;
    }

    /// <summary>Returns the current application configuration. Secrets are masked.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(AppConfigurationDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Get()
    {
        var config = await _configService.GetConfigurationAsync();
        return Ok(config);
    }

    /// <summary>Saves (creates or updates) the application configuration.</summary>
    [HttpPut]
    [ProducesResponseType(typeof(AppConfigurationDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Update([FromBody] UpdateAppConfigurationRequest request)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);

        var user = User.FindFirstValue(ClaimTypes.Email)
                   ?? User.FindFirstValue("preferred_username")
                   ?? User.FindFirstValue(ClaimTypes.NameIdentifier)
                   ?? "unknown";

        var result = await _configService.UpdateConfigurationAsync(request, user);
        return Ok(result);
    }
}
