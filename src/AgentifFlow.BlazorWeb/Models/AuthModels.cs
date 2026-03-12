namespace AgentifFlow.BlazorWeb.Models;

/// <summary>Request body for POST /api/auth/local-login.</summary>
public record LocalLoginRequest(string Username, string Password);

/// <summary>Response from POST /api/auth/local-login containing the bearer JWT.</summary>
public record LocalLoginResponse(string Token, DateTime ExpiresAt);
