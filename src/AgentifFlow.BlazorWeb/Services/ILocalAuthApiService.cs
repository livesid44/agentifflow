using AgentifFlow.BlazorWeb.Models;

namespace AgentifFlow.BlazorWeb.Services;

public interface ILocalAuthApiService
{
    /// <summary>
    /// Calls POST /api/auth/local-login and returns the response, or null on failure.
    /// </summary>
    Task<LocalLoginResponse?> LoginAsync(string username, string password);
}
