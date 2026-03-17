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
builder.Services.AddHttpClient(); // used by ThirdPartyApiIntegration skill
builder.Services.AddScoped<IAgentTaskService, AgentTaskService>();
builder.Services.AddScoped<IAppConfigurationService, AppConfigurationService>();
builder.Services.AddScoped<IBlobWatcherJobService, BlobWatcherJobService>();
builder.Services.AddScoped<IAgentService, AgentService>();
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

            // Multi-agent: BlobWatcherJobs.AgentId (added in 20260316000000_AddAgents)
            await EnsureSqliteColumnAsync(db, startupLogger,
                table: "BlobWatcherJobs",   column: "AgentId",                 definition: "INTEGER NULL");

            // Multi-agent: Agents table (added in 20260316000000_AddAgents)
            await EnsureSqliteTableAsync(db, startupLogger, "Agents", @"
                CREATE TABLE IF NOT EXISTS ""Agents"" (
                    ""Id""                       INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                    ""Name""                     TEXT    NOT NULL DEFAULT '',
                    ""Description""              TEXT    NULL,
                    ""IsEnabled""                INTEGER NOT NULL DEFAULT 1,
                    ""BlobContainerName""        TEXT    NULL,
                    ""NotificationEmail""        TEXT    NULL,
                    ""NotifyOnSuccess""          INTEGER NOT NULL DEFAULT 0,
                    ""NotifyOnFileNotFound""     INTEGER NOT NULL DEFAULT 1,
                    ""NotifyOnDataIssue""        INTEGER NOT NULL DEFAULT 1,
                    ""MaxRetryCount""            INTEGER NOT NULL DEFAULT 3,
                    ""AutoRetryIntervalMinutes"" INTEGER NOT NULL DEFAULT 30,
                    ""SqlPushEnabled""           INTEGER NOT NULL DEFAULT 0,
                    ""SqlTargetTable""           TEXT    NULL,
                    ""SqlColumnMappingJson""     TEXT    NULL,
                    ""BlobArchiveFilePattern""   TEXT    NULL,
                    ""BlobArchiveAppendDate""    INTEGER NOT NULL DEFAULT 1,
                    ""CreatedAt""                TEXT    NOT NULL DEFAULT '',
                    ""UpdatedAt""                TEXT    NOT NULL DEFAULT ''
                );");

            // Multi-agent: AgentFileTargets table (added in 20260316000000_AddAgents)
            await EnsureSqliteTableAsync(db, startupLogger, "AgentFileTargets", @"
                CREATE TABLE IF NOT EXISTS ""AgentFileTargets"" (
                    ""Id""          INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                    ""AgentId""     INTEGER NOT NULL,
                    ""FilePattern"" TEXT    NOT NULL DEFAULT '',
                    ""AppendDate""  INTEGER NOT NULL DEFAULT 1,
                    ""IsRequired""  INTEGER NOT NULL DEFAULT 1,
                    CONSTRAINT ""FK_AgentFileTargets_Agents"" FOREIGN KEY (""AgentId"")
                        REFERENCES ""Agents"" (""Id"") ON DELETE CASCADE
                );");

            // Skills: AgentSkills table (added in 20260316010000_AddAgentSkills)
            await EnsureSqliteTableAsync(db, startupLogger, "AgentSkills", @"
                CREATE TABLE IF NOT EXISTS ""AgentSkills"" (
                    ""Id""         INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                    ""AgentId""    INTEGER NOT NULL,
                    ""SkillType""  TEXT    NOT NULL DEFAULT '',
                    ""IsEnabled""  INTEGER NOT NULL DEFAULT 1,
                    ""ConfigJson"" TEXT    NULL,
                    CONSTRAINT ""FK_AgentSkills_Agents"" FOREIGN KEY (""AgentId"")
                        REFERENCES ""Agents"" (""Id"") ON DELETE CASCADE
                );");

            // Job monitor: MonitoredJobs table
            await EnsureSqliteTableAsync(db, startupLogger, "MonitoredJobs", @"
                CREATE TABLE IF NOT EXISTS ""MonitoredJobs"" (
                    ""Id""            INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                    ""JobName""       TEXT    NOT NULL DEFAULT '',
                    ""ProjectName""   TEXT    NULL,
                    ""Status""        TEXT    NOT NULL DEFAULT 'Running',
                    ""FailureReason"" TEXT    NULL,
                    ""Logs""          TEXT    NULL,
                    ""StartedAt""     TEXT    NOT NULL DEFAULT '',
                    ""CompletedAt""   TEXT    NULL,
                    ""UpdatedAt""     TEXT    NOT NULL DEFAULT ''
                );");
        }
        // ── Seed built-in demo agents ─────────────────────────────────────────
        await AgentifFlow.Api.Services.AgentSeedService.SeedNerandomilastAgentAsync(
            db, startupLogger, CancellationToken.None);
        await AgentifFlow.Api.Services.AgentSeedService.SeedAzkabanJobMonitorAgentAsync(
            db, startupLogger, CancellationToken.None);

        // ── Startup probe jobs (one per enabled agent) ────────────────────────
        // For agents that require AI (LogAnalysis skill), the probe job is marked
        // Failed with "No AI configuration found" when OpenAI has not been set up.
        await AgentifFlow.Api.Services.AgentSeedService.SeedStartupJobsAsync(
            db, startupLogger, CancellationToken.None);
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

/// <summary>
/// Ensures a SQLite table exists, creating it if absent.
/// Uses CREATE TABLE IF NOT EXISTS so it is fully idempotent.
/// </summary>
static async Task EnsureSqliteTableAsync(
    AgentifFlow.Api.Data.AgentifFlowDbContext db,
    ILogger logger,
    string tableName,
    string createSql)
{
    var conn = db.Database.GetDbConnection();
    var shouldClose = conn.State != System.Data.ConnectionState.Open;
    if (shouldClose) await conn.OpenAsync();
    try
    {
        using var checkCmd = conn.CreateCommand();
        checkCmd.CommandText =
            $"SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='{tableName}'";
        var exists = Convert.ToInt32(await checkCmd.ExecuteScalarAsync()) > 0;

        if (!exists)
        {
            logger.LogWarning(
                "Schema repair: table '{Table}' missing — creating it now.", tableName);
            using var createCmd = conn.CreateCommand();
            createCmd.CommandText = createSql;
            await createCmd.ExecuteNonQueryAsync();
        }
    }
    finally
    {
        if (shouldClose) await conn.CloseAsync();
    }
}
