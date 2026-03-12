using AgentifFlow.Api.Models;
using AgentifFlow.Api.Services;
using Azure.AI.OpenAI;
using Azure.Storage.Blobs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.Identity.Client;
using Microsoft.Identity.Web.Resource;
using OpenAI.Chat;

namespace AgentifFlow.Api.Controllers;

/// <summary>
/// Provides connectivity tests for each configured external service.
/// Each endpoint reads the saved credentials and attempts a lightweight
/// round-trip to verify that the settings are correct.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
[RequiredScope(RequiredScopesConfigurationKey = "AzureAd:Scopes")]
public class ConnectivityController : ControllerBase
{
    private readonly IAppConfigurationService _configService;
    private readonly ILogger<ConnectivityController> _logger;

    public ConnectivityController(
        IAppConfigurationService configService,
        ILogger<ConnectivityController> logger)
    {
        _configService = configService;
        _logger = logger;
    }

    // ── Graph API ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Tests Microsoft Graph API connectivity by acquiring a client-credentials
    /// token.  A successful token acquisition confirms that the Tenant ID,
    /// Client ID, and Client Secret are valid and that the app registration is
    /// correctly configured.
    /// </summary>
    [HttpPost("graph")]
    [ProducesResponseType(typeof(ConnectivityResult), StatusCodes.Status200OK)]
    public async Task<IActionResult> TestGraph()
    {
        var (tenantId, clientId, clientSecret, _) =
            await _configService.GetGraphRawSettingsAsync();

        if (string.IsNullOrWhiteSpace(tenantId) ||
            string.IsNullOrWhiteSpace(clientId) ||
            string.IsNullOrWhiteSpace(clientSecret))
        {
            return Ok(new ConnectivityResult
            {
                Success = false,
                Message = "Graph API is not configured. Please save Tenant ID, Client ID and Client Secret first."
            });
        }

        try
        {
            var app = ConfidentialClientApplicationBuilder
                .Create(clientId)
                .WithClientSecret(clientSecret)
                .WithAuthority($"https://login.microsoftonline.com/{tenantId}")
                .Build();

            var result = await app.AcquireTokenForClient(
                ["https://graph.microsoft.com/.default"]).ExecuteAsync();

            return Ok(new ConnectivityResult
            {
                Success = true,
                Message = $"Connected successfully. Token acquired (expires {result.ExpiresOn:HH:mm UTC})."
            });
        }
        catch (MsalException msalEx)
        {
            _logger.LogWarning(msalEx, "Graph connectivity test failed (MSAL)");
            return Ok(new ConnectivityResult
            {
                Success = false,
                Message = $"Authentication failed: {msalEx.Message}"
            });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Graph connectivity test failed");
            return Ok(new ConnectivityResult
            {
                Success = false,
                Message = $"Connection failed: {ex.Message}"
            });
        }
    }

    // ── Azure OpenAI ─────────────────────────────────────────────────────────

    /// <summary>
    /// Tests Azure OpenAI connectivity by listing the available deployments on
    /// the configured resource.  Confirms that the endpoint and API key are correct.
    /// </summary>
    [HttpPost("openai")]
    [ProducesResponseType(typeof(ConnectivityResult), StatusCodes.Status200OK)]
    public async Task<IActionResult> TestOpenAi()
    {
        var (endpoint, apiKey, deploymentName) =
            await _configService.GetOpenAiRawSettingsAsync();

        if (string.IsNullOrWhiteSpace(endpoint) || string.IsNullOrWhiteSpace(apiKey))
        {
            return Ok(new ConnectivityResult
            {
                Success = false,
                Message = "Azure OpenAI is not configured. Please save the Endpoint URL and API Key first."
            });
        }

        try
        {
            // Use the Chat Completions API with a minimal prompt to verify connectivity.
            var client = new AzureOpenAIClient(
                new Uri(endpoint),
                new Azure.AzureKeyCredential(apiKey));

            var deployment = string.IsNullOrWhiteSpace(deploymentName) ? "gpt-4o" : deploymentName;
            var chatClient = client.GetChatClient(deployment);

            var response = await chatClient.CompleteChatAsync(
                [new UserChatMessage("ping")],
                new ChatCompletionOptions { MaxOutputTokenCount = 1 });

            return Ok(new ConnectivityResult
            {
                Success = true,
                Message = $"Connected successfully to deployment '{deployment}'."
            });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "OpenAI connectivity test failed");
            return Ok(new ConnectivityResult
            {
                Success = false,
                Message = $"Connection failed: {ex.Message}"
            });
        }
    }

    // ── Azure Blob Storage ────────────────────────────────────────────────────

    /// <summary>
    /// Tests Azure Blob Storage connectivity by checking whether the configured
    /// container exists.  Confirms that the connection string and container name
    /// are valid and that the storage account is reachable.
    /// </summary>
    [HttpPost("blob")]
    [ProducesResponseType(typeof(ConnectivityResult), StatusCodes.Status200OK)]
    public async Task<IActionResult> TestBlob()
    {
        var connStr = await _configService.GetBlobRawSettingsAsync();
        var dto = await _configService.GetConfigurationAsync();
        var containerName = dto.BlobContainerName;

        if (string.IsNullOrWhiteSpace(connStr))
        {
            return Ok(new ConnectivityResult
            {
                Success = false,
                Message = "Blob Storage is not configured. Please save the Connection String first."
            });
        }

        try
        {
            if (!string.IsNullOrWhiteSpace(containerName))
            {
                var containerClient = new BlobContainerClient(connStr, containerName);
                var exists = await containerClient.ExistsAsync();
                return Ok(new ConnectivityResult
                {
                    Success = true,
                    Message = exists.Value
                        ? $"Connected successfully. Container '{containerName}' exists."
                        : $"Connected to storage account. Container '{containerName}' does not exist yet — it will be created on first use."
                });
            }
            else
            {
                // No container specified — just validate the account-level connection
                var serviceClient = new BlobServiceClient(connStr);
                var props = await serviceClient.GetPropertiesAsync();
                return Ok(new ConnectivityResult
                {
                    Success = true,
                    Message = "Connected to storage account successfully."
                });
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Blob connectivity test failed");
            return Ok(new ConnectivityResult
            {
                Success = false,
                Message = $"Connection failed: {ex.Message}"
            });
        }
    }

    // ── SQL Database ──────────────────────────────────────────────────────────

    /// <summary>
    /// Tests SQL Database connectivity by opening and immediately closing a
    /// SqlConnection with the saved connection string.
    /// </summary>
    [HttpPost("sql")]
    [ProducesResponseType(typeof(ConnectivityResult), StatusCodes.Status200OK)]
    public async Task<IActionResult> TestSql()
    {
        var connStr = await _configService.GetSqlRawSettingsAsync();

        if (string.IsNullOrWhiteSpace(connStr))
        {
            return Ok(new ConnectivityResult
            {
                Success = false,
                Message = "SQL Database is not configured. Please save the Connection String first."
            });
        }

        try
        {
            using var conn = new SqlConnection(connStr);
            await conn.OpenAsync();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT @@VERSION";
            cmd.CommandTimeout = 10;
            var version = (string?)await cmd.ExecuteScalarAsync();

            return Ok(new ConnectivityResult
            {
                Success = true,
                Message = $"Connected successfully. Server: {version?.Split('\n')[0].Trim() ?? "SQL Server"}."
            });
        }
        catch (SqlException sqlEx)
        {
            _logger.LogWarning(sqlEx, "SQL connectivity test failed");
            return Ok(new ConnectivityResult
            {
                Success = false,
                Message = $"SQL connection failed (error {sqlEx.Number}): {sqlEx.Message}"
            });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "SQL connectivity test failed");
            return Ok(new ConnectivityResult
            {
                Success = false,
                Message = $"Connection failed: {ex.Message}"
            });
        }
    }
}
