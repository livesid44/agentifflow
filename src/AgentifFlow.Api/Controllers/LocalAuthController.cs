using AgentifFlow.Api.Models;
using AgentifFlow.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AgentifFlow.Api.Controllers;

/// <summary>
/// Local (hardcoded-credentials) authentication endpoint.
/// Issues a short-lived JWT for the single configured username/password credential.
/// Only active when <c>DevAuth:Enabled = true</c> in configuration; returns 404 otherwise,
/// so the endpoint is never reachable when local auth is not explicitly enabled.
/// </summary>
[ApiController]
[Route("api/auth")]
[AllowAnonymous]
public class LocalAuthController : ControllerBase
{
    private readonly ILocalAuthService _authService;
    private readonly ILogger<LocalAuthController> _logger;

    public LocalAuthController(
        ILocalAuthService authService,
        ILogger<LocalAuthController> logger)
    {
        _authService = authService;
        _logger      = logger;
    }

    /// <summary>
    /// Authenticates with a local username/password and returns a bearer JWT.
    /// Only available when <c>DevAuth:Enabled = true</c> in configuration.
    /// </summary>
    [HttpPost("local-login")]
    [ProducesResponseType(typeof(LocalLoginResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public IActionResult LocalLogin([FromBody] LocalLoginRequest request)
    {
        // If the service has no signing key, DevAuth is not enabled/configured.
        if (!_authService.IsEnabled)
            return NotFound();

        _logger.LogInformation(
            "Local-login attempt for user '{Username}'", request.Username);

        var result = _authService.Authenticate(request.Username, request.Password);
        if (result is null)
        {
            _logger.LogWarning(
                "Local-login failed for user '{Username}'", request.Username);
            return Unauthorized(new { error = "Invalid credentials." });
        }

        _logger.LogInformation(
            "Local-login succeeded for user '{Username}'", request.Username);
        return Ok(result);
    }
}
