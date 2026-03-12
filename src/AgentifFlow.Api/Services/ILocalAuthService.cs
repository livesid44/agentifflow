using AgentifFlow.Api.Models;

namespace AgentifFlow.Api.Services;

public interface ILocalAuthService
{
    /// <summary>
    /// Validates username/password against the DevAuth configuration and returns
    /// a signed JWT on success, or <c>null</c> when credentials are wrong or DevAuth is disabled.
    /// </summary>
    LocalLoginResponse? Authenticate(string username, string password);
}
