using System.Net.Http.Headers;

namespace AgentifFlow.BlazorWeb.Services;

/// <summary>
/// HTTP delegating handler that attaches the dev JWT from <see cref="LocalAuthStateProvider"/>
/// to every outgoing request as a Bearer token.
/// Used instead of <see cref="Microsoft.AspNetCore.Components.WebAssembly.Authentication.AuthorizationMessageHandler"/>
/// when dev local auth is active.
/// </summary>
public class DevAuthHandler : DelegatingHandler
{
    private readonly LocalAuthStateProvider _authProvider;

    public DevAuthHandler(LocalAuthStateProvider authProvider) => _authProvider = authProvider;

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var token = await _authProvider.GetTokenAsync();
        if (!string.IsNullOrEmpty(token))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        return await base.SendAsync(request, cancellationToken);
    }
}
