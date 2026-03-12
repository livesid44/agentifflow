using AgentifFlow.Api.Models;
using AgentifFlow.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AgentifFlow.Api.Controllers;

/// <summary>
/// Developer-only authentication endpoint.
/// Issues a short-lived JWT for a single configured username/password credential.
/// Returns 404 in all non-Development environments so the endpoint is never reachable
/// in staging or production.
/// </summary>
[ApiController]
[Route("api/auth")]
[AllowAnonymous]
public class LocalAuthController : ControllerBase
{
    private readonly ILocalAuthService _authService;
    private readonly IWebHostEnvironment _env;
    private readonly ILogger<LocalAuthController> _logger;

    public LocalAuthController(
        ILocalAuthService authService,
        IWebHostEnvironment env,
        ILogger<LocalAuthController> logger)
    {
        _authService = authService;
        _env         = env;
        _logger      = logger;
    }

    /// <summary>
    /// Authenticates with a local username/password and returns a bearer JWT.
    /// Only available in the Development environment.
    /// </summary>
    [HttpPost("local-login")]
    [ProducesResponseType(typeof(LocalLoginResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public IActionResult LocalLogin([FromBody] LocalLoginRequest request)
    {
        if (!_env.IsDevelopment())
            return NotFound(); // Completely hidden from non-development environments

        _logger.LogInformation(
            "Dev local-login attempt for user '{Username}'", request.Username);

        var result = _authService.Authenticate(request.Username, request.Password);
        if (result is null)
        {
            _logger.LogWarning(
                "Dev local-login failed for user '{Username}'", request.Username);
            return Unauthorized(new { error = "Invalid credentials." });
        }

        _logger.LogInformation(
            "Dev local-login succeeded for user '{Username}'", request.Username);
        return Ok(result);
    }
}
