using AgentifFlow.Api.Data;
using AgentifFlow.Api.Services;
using Azure.AI.OpenAI;
using Microsoft.EntityFrameworkCore;
using Microsoft.Identity.Web;
using Microsoft.OpenApi.Models;

var builder = WebApplication.CreateBuilder(args);

// ── Azure AD / OAuth2.0 authentication ──────────────────────────────────────
builder.Services.AddMicrosoftIdentityWebApiAuthentication(builder.Configuration)
    .EnableTokenAcquisitionToCallDownstreamApi()
    .AddMicrosoftGraph(builder.Configuration.GetSection("Graph"))
    .AddInMemoryTokenCaches();

// ── SQL Server / EF Core ─────────────────────────────────────────────────────
builder.Services.AddDbContext<AgentifFlowDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

// ── Azure OpenAI LLM client ─────────────────────────────────────────────────
builder.Services.AddSingleton<AzureOpenAIClient>(_ =>
{
    var endpoint = builder.Configuration["AzureOpenAI:Endpoint"]
        ?? throw new InvalidOperationException("AzureOpenAI:Endpoint is not configured.");
    var apiKey = builder.Configuration["AzureOpenAI:ApiKey"]
        ?? throw new InvalidOperationException("AzureOpenAI:ApiKey is not configured.");
    return new AzureOpenAIClient(new Uri(endpoint), new System.ClientModel.ApiKeyCredential(apiKey));
});

// ── Application services ─────────────────────────────────────────────────────
builder.Services.AddScoped<IGraphMailService, GraphMailService>();
builder.Services.AddScoped<ILlmService, LlmService>();
builder.Services.AddScoped<IAgentTaskService, AgentTaskService>();

// ── MVC / API ────────────────────────────────────────────────────────────────
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

    // OAuth2 implicit flow for Swagger UI
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
});

var app = builder.Build();

// ── Auto-migrate database on startup ────────────────────────────────────────
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AgentifFlowDbContext>();
    db.Database.EnsureCreated();
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

app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

app.Run();
