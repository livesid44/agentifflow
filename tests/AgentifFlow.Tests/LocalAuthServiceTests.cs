using System.IdentityModel.Tokens.Jwt;
using AgentifFlow.Api.Services;
using Microsoft.Extensions.Configuration;

namespace AgentifFlow.Tests;

public class LocalAuthServiceTests
{
    private const string ValidUsername = "admin";
    private const string ValidPassword = "Admin@123";
    private const string SigningKey     = "TestSigningKeyThatIsAtLeast32BytesLong!!";

    private static LocalAuthService CreateService(
        bool enabled     = true,
        string username  = ValidUsername,
        string password  = ValidPassword,
        string signingKey = SigningKey)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DevAuth:Enabled"]       = enabled.ToString(),
                ["DevAuth:Username"]      = username,
                ["DevAuth:Password"]      = password,
                ["DevAuth:JwtSigningKey"] = signingKey,
            })
            .Build();

        return new LocalAuthService(config);
    }

    [Fact]
    public void Authenticate_ValidCredentials_ReturnsToken()
    {
        var svc = CreateService();

        var result = svc.Authenticate(ValidUsername, ValidPassword);

        Assert.NotNull(result);
        Assert.False(string.IsNullOrEmpty(result.Token));
        Assert.True(result.ExpiresAt > DateTime.UtcNow);
    }

    [Fact]
    public void Authenticate_WrongPassword_ReturnsNull()
    {
        var svc = CreateService();

        var result = svc.Authenticate(ValidUsername, "wrongpassword");

        Assert.Null(result);
    }

    [Fact]
    public void Authenticate_WrongUsername_ReturnsNull()
    {
        var svc = CreateService();

        var result = svc.Authenticate("notadmin", ValidPassword);

        Assert.Null(result);
    }

    [Fact]
    public void Authenticate_WhenDisabled_ReturnsNull()
    {
        var svc = CreateService(enabled: false);

        var result = svc.Authenticate(ValidUsername, ValidPassword);

        Assert.Null(result);
    }

    [Fact]
    public void Authenticate_EmptyUsername_ReturnsNull()
    {
        var svc = CreateService();

        var result = svc.Authenticate("", ValidPassword);

        Assert.Null(result);
    }

    [Fact]
    public void Authenticate_EmptyPassword_ReturnsNull()
    {
        var svc = CreateService();

        var result = svc.Authenticate(ValidUsername, "");

        Assert.Null(result);
    }

    [Fact]
    public void Authenticate_ValidCredentials_TokenHasExpectedClaims()
    {
        var svc = CreateService();

        var result = svc.Authenticate(ValidUsername, ValidPassword);
        Assert.NotNull(result);

        // Parse the JWT without validation so we can inspect claims
        var handler = new JwtSecurityTokenHandler();
        var jwt     = handler.ReadJwtToken(result.Token);

        Assert.Equal("agentifflow-dev",  jwt.Issuer);
        Assert.Contains(jwt.Audiences,   a => a == "agentifflow-api");
        Assert.Contains(jwt.Claims, c => c.Type == "name" && c.Value == ValidUsername);
        Assert.Contains(jwt.Claims, c => c.Type == "sub"  && c.Value == ValidUsername);
        // Scope claim that satisfies [RequiredScope("access_as_user")] on API controllers
        Assert.Contains(jwt.Claims, c => c.Type == "scp"  && c.Value == "access_as_user");
    }

    [Fact]
    public void Authenticate_ValidCredentials_TokenExpiresInFuture()
    {
        var svc = CreateService();

        var result = svc.Authenticate(ValidUsername, ValidPassword);
        Assert.NotNull(result);

        // Token should expire at least 7 hours from now (configured: 8 hours)
        Assert.True(result.ExpiresAt > DateTime.UtcNow.AddHours(7));
    }

    [Fact]
    public void Authenticate_CaseSensitiveUsername()
    {
        var svc = CreateService();

        // Usernames are case-sensitive
        var result = svc.Authenticate("Admin", ValidPassword);
        Assert.Null(result);
    }

    [Fact]
    public void Authenticate_CaseSensitivePassword()
    {
        var svc = CreateService();

        // Passwords are case-sensitive
        var result = svc.Authenticate(ValidUsername, "admin@123");
        Assert.Null(result);
    }
}
