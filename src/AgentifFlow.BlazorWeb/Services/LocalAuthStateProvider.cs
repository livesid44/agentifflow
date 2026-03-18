using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.JSInterop;

namespace AgentifFlow.BlazorWeb.Services;

/// <summary>
/// Custom <see cref="AuthenticationStateProvider"/> used in development mode.
/// Persists the JWT in <c>sessionStorage</c> so the session survives page refreshes
/// but is cleared when the browser tab is closed.
/// </summary>
public class LocalAuthStateProvider : AuthenticationStateProvider
{
    private const string TokenKey = "agentifflow_dev_token";
    private readonly IJSRuntime _js;

    public LocalAuthStateProvider(IJSRuntime js) => _js = js;

    public override async Task<AuthenticationState> GetAuthenticationStateAsync()
    {
        try
        {
            var token = await _js.InvokeAsync<string?>("sessionStorage.getItem", TokenKey);
            if (string.IsNullOrEmpty(token))
                return Unauthenticated();

            var claims = ParseClaimsFromJwt(token).ToList();

            // Check token expiry — an expired token should not authenticate the user.
            // The "exp" JWT claim is a Unix timestamp (seconds since epoch).
            var expClaim = claims.FirstOrDefault(c => c.Type == "exp");
            if (expClaim is not null && long.TryParse(expClaim.Value, out var expUnix))
            {
                var expiry = DateTimeOffset.FromUnixTimeSeconds(expUnix);
                if (expiry <= DateTimeOffset.UtcNow)
                {
                    // Token has expired — clear it so the login page is shown.
                    await _js.InvokeVoidAsync("sessionStorage.removeItem", TokenKey);
                    return Unauthenticated();
                }
            }

            var identity = new ClaimsIdentity(claims, "DevLocal");
            return new AuthenticationState(new ClaimsPrincipal(identity));
        }
        catch
        {
            return Unauthenticated();
        }
    }

    /// <summary>Stores the token and notifies subscribers that auth state changed.</summary>
    public async Task LoginAsync(string token)
    {
        await _js.InvokeVoidAsync("sessionStorage.setItem", TokenKey, token);
        NotifyAuthenticationStateChanged(GetAuthenticationStateAsync());
    }

    /// <summary>Removes the token and notifies subscribers that auth state changed.</summary>
    public async Task LogoutAsync()
    {
        await _js.InvokeVoidAsync("sessionStorage.removeItem", TokenKey);
        NotifyAuthenticationStateChanged(GetAuthenticationStateAsync());
    }

    /// <summary>Returns the stored JWT, or null when not logged in.</summary>
    public async Task<string?> GetTokenAsync()
        => await _js.InvokeAsync<string?>("sessionStorage.getItem", TokenKey);

    private static AuthenticationState Unauthenticated()
        => new(new ClaimsPrincipal(new ClaimsIdentity()));

    /// <summary>Decodes the JWT payload (base64url) and converts it to Claims.</summary>
    private static IEnumerable<Claim> ParseClaimsFromJwt(string jwt)
    {
        var parts = jwt.Split('.');
        if (parts.Length != 3) return [];

        // JWT uses base64url encoding — convert to standard base64 then pad.
        var payload = parts[1].Replace('-', '+').Replace('_', '/');
        var rem = payload.Length % 4;
        if (rem == 2) payload += "==";
        else if (rem == 3) payload += "=";

        try
        {
            var bytes = Convert.FromBase64String(payload);
            var dict  = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(bytes) ?? [];
            return dict.Select(kv => new Claim(kv.Key, kv.Value.ToString()));
        }
        catch
        {
            return [];
        }
    }
}
