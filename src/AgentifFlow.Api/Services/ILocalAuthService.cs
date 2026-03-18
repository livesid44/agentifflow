using AgentifFlow.Api.Models;

namespace AgentifFlow.Api.Services;

public interface ILocalAuthService
{
    /// <summary>
    /// <c>true</c> when <c>DevAuth:Enabled = true</c> and all required credentials are
    /// present in configuration; <c>false</c> when local auth is not configured.
    /// </summary>
    bool IsEnabled { get; }

    /// <summary>
    /// Validates username/password against the DevAuth configuration and returns
    /// a signed JWT on success, or <c>null</c> when credentials are wrong or DevAuth is disabled.
    /// </summary>
    LocalLoginResponse? Authenticate(string username, string password);
}
