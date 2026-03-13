using AgentifFlow.Api.Data;
using AgentifFlow.Api.Hubs;
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

// ── Dev local auth (development-only username/password JWT) ──────────────────
// The LocalAuthService is always registered; it is a no-op when DevAuth:Enabled = false.
builder.Services.AddSingleton<ILocalAuthService, LocalAuthService>();

var devAuthEnabled = builder.Configuration.GetValue<bool>("DevAuth:Enabled");
if (devAuthEnabled)
{
    // ── Dev mode: skip Azure AD entirely to avoid OIDC discovery errors ──────
    // The DevLocal JWT is registered as the sole "Bearer" scheme.  No network
    // call is needed to validate these tokens, so requests are fast and silent.
    var devJwtKey = builder.Configuration["DevAuth:JwtSigningKey"]
        ?? throw new InvalidOperationException(
            "DevAuth:JwtSigningKey must be configured when DevAuth:Enabled is true.");

    builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
        .AddJwtBearer(options =>
        {
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidIssuer      = "agentifflow-dev",
                ValidAudience    = "agentifflow-api",
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(devJwtKey)),
                ValidateLifetime = true,
                ClockSkew        = TimeSpan.FromSeconds(30),
            };
        });

    builder.Services.AddAuthorization();

    // Register a stub GraphServiceClient so DI resolves correctly in dev mode.
    // Real Graph calls (email send/read) will return 401 without real credentials —
    // that is expected and handled gracefully by the services that use it.
    builder.Services.AddScoped<Microsoft.Graph.GraphServiceClient>(_ =>
        new Microsoft.Graph.GraphServiceClient(
            new Microsoft.Kiota.Abstractions.Authentication.AnonymousAuthenticationProvider()));
}
else
{
    // ── Production mode: full Azure AD / Microsoft Identity auth ─────────────
    builder.Services.AddMicrosoftIdentityWebApiAuthentication(builder.Configuration)
        .EnableTokenAcquisitionToCallDownstreamApi()
        .AddMicrosoftGraph(builder.Configuration.GetSection("Graph"))
        .AddInMemoryTokenCaches();

    builder.Services.AddAuthorization();
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
builder.Services.AddScoped<IBlobWatcherJobService, BlobWatcherJobService>();
builder.Services.AddScoped<ICsvValidationService, CsvValidationService>();
builder.Services.AddHostedService<BlobWatcherBackgroundService>();

// ── SignalR (real-time agent notifications) ───────────────────────────────────
builder.Services.AddSignalR();
builder.Services.AddSingleton<IAgentNotificationService, AgentNotificationService>();

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
// MigrateAsync applies any pending migrations, creating or updating the schema
// for both new installs (fresh DB) and existing installs (old schema).
using (var scope = app.Services.CreateScope())
{
    var startupLogger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
    try
    {
        var db = scope.ServiceProvider.GetRequiredService<AgentifFlowDbContext>();
        await db.Database.MigrateAsync();

        // ── SQLite schema safety net ─────────────────────────────────────────
        // EF can record a migration as applied (in __EFMigrationsHistory) without
        // the DDL actually executing on the live file — for example when the DB was
        // pre-created with EnsureCreated or restored from a backup taken before the
        // migration ran.  We defend against this by explicitly checking each column
        // that was added after the initial schema and adding it when absent.
        if (db.Database.ProviderName == "Microsoft.EntityFrameworkCore.Sqlite")
        {
            await EnsureSqliteColumnAsync(db, startupLogger,
                table: "AppConfigurations",
                column: "GraphMailboxAddress",
                definition: "TEXT NULL");

            // AgentTask email-tracking columns (added in 20260312180000_AddAgentTaskEmailFields)
            await EnsureSqliteColumnAsync(db, startupLogger,
                table: "AgentTasks", column: "SourceEmailId",      definition: "TEXT NULL");
            await EnsureSqliteColumnAsync(db, startupLogger,
                table: "AgentTasks", column: "SourceEmailFrom",    definition: "TEXT NULL");
            await EnsureSqliteColumnAsync(db, startupLogger,
                table: "AgentTasks", column: "SourceEmailSubject", definition: "TEXT NULL");
            await EnsureSqliteColumnAsync(db, startupLogger,
                table: "AgentTasks", column: "ConversationId",     definition: "TEXT NULL");

            // BlobWatcherJob notification-ref columns (added in 20260312190000_AddBlobJobNotificationRef)
            await EnsureSqliteColumnAsync(db, startupLogger,
                table: "BlobWatcherJobs", column: "NotificationRef", definition: "TEXT NULL");
            await EnsureSqliteColumnAsync(db, startupLogger,
                table: "BlobWatcherJobs", column: "UserReply",       definition: "TEXT NULL");

            // Agent Designer columns (added in 20260312200000_AddAgentDesignerFields)
            await EnsureSqliteColumnAsync(db, startupLogger,
                table: "AppConfigurations", column: "BlobEnabled",             definition: "INTEGER NOT NULL DEFAULT 0");
            await EnsureSqliteColumnAsync(db, startupLogger,
                table: "AppConfigurations", column: "BlobReadEmailId",         definition: "TEXT NULL");
            await EnsureSqliteColumnAsync(db, startupLogger,
                table: "AppConfigurations", column: "BlobReadEmailAppendDate", definition: "INTEGER NOT NULL DEFAULT 0");
            await EnsureSqliteColumnAsync(db, startupLogger,
                table: "AppConfigurations", column: "BlobInputFilePattern",    definition: "TEXT NULL");
            await EnsureSqliteColumnAsync(db, startupLogger,
                table: "AppConfigurations", column: "BlobInputAppendDate",     definition: "INTEGER NOT NULL DEFAULT 1");
            await EnsureSqliteColumnAsync(db, startupLogger,
                table: "AppConfigurations", column: "BlobArchiveFilePattern",  definition: "TEXT NULL");
            await EnsureSqliteColumnAsync(db, startupLogger,
                table: "AppConfigurations", column: "BlobArchiveAppendDate",   definition: "INTEGER NOT NULL DEFAULT 1");
            await EnsureSqliteColumnAsync(db, startupLogger,
                table: "AppConfigurations", column: "NotifyOnSuccess",         definition: "INTEGER NOT NULL DEFAULT 0");
            await EnsureSqliteColumnAsync(db, startupLogger,
                table: "AppConfigurations", column: "NotifyOnFileNotFound",    definition: "INTEGER NOT NULL DEFAULT 1");
            await EnsureSqliteColumnAsync(db, startupLogger,
                table: "AppConfigurations", column: "NotifyOnDataIssue",       definition: "INTEGER NOT NULL DEFAULT 1");
            await EnsureSqliteColumnAsync(db, startupLogger,
                table: "AppConfigurations", column: "SqlPushEnabled",          definition: "INTEGER NOT NULL DEFAULT 0");
            await EnsureSqliteColumnAsync(db, startupLogger,
                table: "AppConfigurations", column: "SqlTargetTable",          definition: "TEXT NULL");
            await EnsureSqliteColumnAsync(db, startupLogger,
                table: "AppConfigurations", column: "SqlColumnMappingJson",    definition: "TEXT NULL");

            // Auto-retry fields (added in 20260313000000_AddAutoRetryFields)
            await EnsureSqliteColumnAsync(db, startupLogger,
                table: "AppConfigurations", column: "AutoRetryIntervalMinutes", definition: "INTEGER NOT NULL DEFAULT 30");
            await EnsureSqliteColumnAsync(db, startupLogger,
                table: "BlobWatcherJobs",   column: "RetryAfterUtc",           definition: "TEXT NULL");
        }
    }
    catch (Exception ex)
    {
        startupLogger.LogWarning(ex,
            "Database migration failed — the app will start but database-dependent " +
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
app.MapHub<AgentNotificationHub>("/hubs/agent");

app.Run();

// ── Local helpers ─────────────────────────────────────────────────────────────

/// <summary>
/// Ensures a column exists in the given SQLite table, adding it if absent.
/// This is a startup safety net for cases where EF Core recorded a migration
/// as applied in __EFMigrationsHistory but the DDL never ran on the live file
/// (e.g. the DB was pre-created, restored from an older backup, or the migration
/// ran on a different file path).
/// </summary>
static async Task EnsureSqliteColumnAsync(
    AgentifFlow.Api.Data.AgentifFlowDbContext db,
    ILogger logger,
    string table,
    string column,
    string definition)
{
    var conn = db.Database.GetDbConnection();
    var shouldClose = conn.State != System.Data.ConnectionState.Open;
    if (shouldClose) await conn.OpenAsync();
    try
    {
        using var cmd = conn.CreateCommand();

        // PRAGMA table_info returns one row per column; COUNT(*) = 0 means absent.
        cmd.CommandText =
            $"SELECT COUNT(*) FROM pragma_table_info('{table}') WHERE name = '{column}'";
        var count = Convert.ToInt32(await cmd.ExecuteScalarAsync());

        if (count == 0)
        {
            logger.LogWarning(
                "Schema repair: column '{Column}' missing from '{Table}' — adding it now.",
                column, table);
            cmd.CommandText = $"ALTER TABLE \"{table}\" ADD COLUMN \"{column}\" {definition}";
            await cmd.ExecuteNonQueryAsync();
        }
    }
    finally
    {
        if (shouldClose) await conn.CloseAsync();
    }
}
