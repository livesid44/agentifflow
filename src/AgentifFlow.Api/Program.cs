using AgentifFlow.Api.Data;
using AgentifFlow.Api.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Identity.Web;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

// ── CORS (allow the React frontend during development) ───────────────────────
var frontendOrigins = "_frontendOrigins";
builder.Services.AddCors(options =>
{
    options.AddPolicy(frontendOrigins, policy =>
    {
        var origins = builder.Configuration.GetSection("AllowedOrigins").Get<string[]>()
                      ?? ["http://localhost:5173", "http://localhost:3000"];
        policy.WithOrigins(origins)
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials();
    });
});

// ── Azure AD / OAuth2.0 authentication ──────────────────────────────────────
builder.Services.AddMicrosoftIdentityWebApiAuthentication(builder.Configuration)
    .EnableTokenAcquisitionToCallDownstreamApi()
    .AddMicrosoftGraph(builder.Configuration.GetSection("Graph"))
    .AddInMemoryTokenCaches();

// ── Dev local auth (development-only username/password JWT) ──────────────────
// The LocalAuthService is always registered; it is a no-op when DevAuth:Enabled = false.
builder.Services.AddSingleton<ILocalAuthService, LocalAuthService>();

var devAuthEnabled = builder.Configuration.GetValue<bool>("DevAuth:Enabled");
if (devAuthEnabled)
{
    var devJwtKey = builder.Configuration["DevAuth:JwtSigningKey"]
        ?? throw new InvalidOperationException(
            "DevAuth:JwtSigningKey must be configured when DevAuth:Enabled is true.");

    // Add a second JWT bearer scheme that validates locally-issued dev tokens.
    builder.Services.AddAuthentication()
        .AddJwtBearer("DevLocal", options =>
        {
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidIssuer            = "agentifflow-dev",
                ValidAudience          = "agentifflow-api",
                IssuerSigningKey       = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(devJwtKey)),
                ValidateLifetime       = true,
                ClockSkew              = TimeSpan.FromSeconds(30),
            };
        });

    // Update the default policy to accept tokens from either Azure AD or DevLocal.
    builder.Services.AddAuthorization(options =>
    {
        options.DefaultPolicy = new AuthorizationPolicyBuilder(
                JwtBearerDefaults.AuthenticationScheme, "DevLocal")
            .RequireAuthenticatedUser()
            .Build();
    });
}

// ── SQL Server / EF Core ─────────────────────────────────────────────────────
// Development uses SQLite (cross-platform, zero-install).
// All other environments use SQL Server.
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? "Data Source=agentifflow-dev.db";

builder.Services.AddDbContext<AgentifFlowDbContext>(options =>
{
    if (builder.Environment.IsDevelopment())
        options.UseSqlite(connectionString);
    else
        options.UseSqlServer(connectionString);
});

// ── Application services ─────────────────────────────────────────────────────
// Note: AzureOpenAIClient is NOT registered as a singleton here.
// LlmService resolves OpenAI credentials on-demand from the database
// (configured via the Integration Settings page in the UI).
builder.Services.AddScoped<IGraphMailService, GraphMailService>();
builder.Services.AddScoped<ILlmService, LlmService>();
builder.Services.AddScoped<IAgentTaskService, AgentTaskService>();
builder.Services.AddScoped<IAppConfigurationService, AppConfigurationService>();

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "AgentifFlow API",
        Version = "v1",
        Description = "AgentifFlow: Azure-integrated AI agent platform with OAuth2, Graph API, LLM, and SQL database."
    });

    // OAuth2 implicit flow for Swagger UI (Azure AD)
    var tenantId = builder.Configuration["AzureAd:TenantId"] ?? "common";
    var clientId = builder.Configuration["AzureAd:ClientId"] ?? string.Empty;

    options.AddSecurityDefinition("oauth2", new OpenApiSecurityScheme
    {
        Type = SecuritySchemeType.OAuth2,
        Flows = new OpenApiOAuthFlows
        {
            Implicit = new OpenApiOAuthFlow
            {
                AuthorizationUrl = new Uri($"https://login.microsoftonline.com/{tenantId}/oauth2/v2.0/authorize"),
                TokenUrl = new Uri($"https://login.microsoftonline.com/{tenantId}/oauth2/v2.0/token"),
                Scopes = new Dictionary<string, string>
                {
                    { $"api://{clientId}/access_as_user", "Access AgentifFlow API" }
                }
            }
        }
    });

    options.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "oauth2" }
            },
            [$"api://{clientId}/access_as_user"]
        }
    });

    // Dev-only bearer token definition (visible in Swagger UI when DevAuth is enabled)
    if (devAuthEnabled)
    {
        options.AddSecurityDefinition("DevLocal", new OpenApiSecurityScheme
        {
            Type        = SecuritySchemeType.Http,
            Scheme      = "bearer",
            BearerFormat = "JWT",
            Description = "Development-only JWT. Obtain via POST /api/auth/local-login.",
        });
        options.AddSecurityRequirement(new OpenApiSecurityRequirement
        {
            {
                new OpenApiSecurityScheme
                {
                    Reference = new OpenApiReference
                        { Type = ReferenceType.SecurityScheme, Id = "DevLocal" }
                },
                []
            }
        });
    }
});

var app = builder.Build();

// ── Auto-migrate database on startup ────────────────────────────────────────
using (var scope = app.Services.CreateScope())
{
    try
    {
        var db = scope.ServiceProvider.GetRequiredService<AgentifFlowDbContext>();
        db.Database.EnsureCreated();
    }
    catch (Exception ex)
    {
        var startupLogger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
        startupLogger.LogWarning(ex,
            "Database initialisation failed — the app will start but database-dependent " +
            "features will be unavailable until the connection is configured.");
    }
}

// ── HTTP pipeline ────────────────────────────────────────────────────────────
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/swagger/v1/swagger.json", "AgentifFlow API v1");
        options.OAuthClientId(builder.Configuration["AzureAd:ClientId"]);
        options.OAuthUsePkce();
    });
}
else
{
    // Only redirect to HTTPS in non-development environments.
    // In Development the API also listens on HTTP (port 5045) so the Blazor dev
    // client can reach it without a dev-cert trust requirement.
    app.UseHttpsRedirection();
}
app.UseCors(frontendOrigins);
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

app.Run();
