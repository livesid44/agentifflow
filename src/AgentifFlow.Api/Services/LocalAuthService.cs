using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using AgentifFlow.Api.Models;
using Microsoft.IdentityModel.Tokens;

namespace AgentifFlow.Api.Services;

/// <summary>
/// Development-only authentication service.
/// Validates a single configured username/password pair and issues a short-lived JWT.
/// The service is a no-op when <c>DevAuth:Enabled</c> is false.
/// </summary>
public class LocalAuthService : ILocalAuthService
{
    // Token lifetime: 8 hours — long enough for a full dev day without re-login.
    private const int TokenExpiryMinutes = 480;

    private readonly string? _username;
    private readonly string? _password;
    private readonly byte[]? _signingKeyBytes;

    public LocalAuthService(IConfiguration configuration)
    {
        var section = configuration.GetSection("DevAuth");
        if (!section.GetValue<bool>("Enabled")) return;

        _username = section["Username"];
        _password = section["Password"];

        var keyStr = section["JwtSigningKey"];
        if (!string.IsNullOrWhiteSpace(keyStr))
            _signingKeyBytes = Encoding.UTF8.GetBytes(keyStr);
    }

    /// <inheritdoc />
    public LocalLoginResponse? Authenticate(string username, string password)
    {
        if (_signingKeyBytes is null
            || string.IsNullOrEmpty(_username)
            || string.IsNullOrEmpty(_password))
            return null; // DevAuth not configured

        // Constant-time comparison prevents timing side-channel attacks.
        if (!SecureEquals(username, _username) || !SecureEquals(password, _password))
            return null;

        return IssueToken(username);
    }

    private LocalLoginResponse IssueToken(string username)
    {
        var key         = new SymmetricSecurityKey(_signingKeyBytes!);
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var expiry      = DateTime.UtcNow.AddMinutes(TokenExpiryMinutes);

        var token = new JwtSecurityToken(
            issuer:    "agentifflow-dev",
            audience:  "agentifflow-api",
            claims:
            [
                new Claim(JwtRegisteredClaimNames.Sub,  username),
                new Claim(JwtRegisteredClaimNames.Name, username),
                new Claim(ClaimTypes.NameIdentifier,    username),
                new Claim(ClaimTypes.Name,              username),
                // Satisfies [RequiredScope("access_as_user")] on all controllers
                new Claim("scp", "access_as_user"),
            ],
            notBefore: DateTime.UtcNow,
            expires:   expiry,
            signingCredentials: credentials);

        var tokenString = new JwtSecurityTokenHandler().WriteToken(token);
        return new LocalLoginResponse(tokenString, expiry);
    }

    /// <summary>Constant-time byte comparison to prevent timing attacks.</summary>
    private static bool SecureEquals(string a, string b)
    {
        var aBytes = Encoding.UTF8.GetBytes(a);
        var bBytes = Encoding.UTF8.GetBytes(b);
        return CryptographicOperations.FixedTimeEquals(aBytes, bBytes);
    }
}
