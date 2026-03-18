using System.ComponentModel.DataAnnotations;

namespace AgentifFlow.Api.Models;

/// <summary>Request body for the developer-only local login endpoint.</summary>
public record LocalLoginRequest(
    [Required, MaxLength(100)] string Username,
    [Required, MaxLength(200)] string Password);

/// <summary>Response containing the bearer token issued by the local login endpoint.</summary>
public record LocalLoginResponse(string Token, DateTime ExpiresAt);
