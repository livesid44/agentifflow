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

    // ── Email send test ───────────────────────────────────────────────────────

    /// <summary>
    /// Validates the mail service end-to-end by actually sending a test email.
    ///
    /// Supply <paramref name="to"/> in the query string to override the recipient;
    /// when omitted the configured <c>NotificationEmail</c> address is used.
    ///
    /// This is the definitive way to confirm that:
    /// <list type="number">
    ///   <item>The Tenant ID / Client ID / Client Secret combination is valid.</item>
    ///   <item>The application has <c>Mail.Send</c> (and optionally <c>Mail.Read</c>) permission.</item>
    ///   <item>The Mailbox Email / UPN is accessible by the application.</item>
    ///   <item>The recipient address is reachable.</item>
    /// </list>
    /// </summary>
    /// <param name="to">Optional override for the test recipient email address.</param>
    [HttpPost("mail")]
    [ProducesResponseType(typeof(ConnectivityResult), StatusCodes.Status200OK)]
    public async Task<IActionResult> TestMailSend(
        [FromServices] IGraphMailService mailService,
        [FromQuery]    string?           to = null)
    {
        var dto = await _configService.GetConfigurationAsync();

        // ── Prerequisite checks ───────────────────────────────────────────────
        if (string.IsNullOrWhiteSpace(dto.GraphTenantId) ||
            string.IsNullOrWhiteSpace(dto.GraphClientId) ||
            dto.GraphClientSecret == null || dto.GraphClientSecret.Length == 0)
        {
            return Ok(new ConnectivityResult
            {
                Success = false,
                Message = "Graph API credentials are not configured. Please save Tenant ID, Client ID and Client Secret on the Integration Settings page first."
            });
        }

        if (string.IsNullOrWhiteSpace(dto.GraphMailboxAddress))
        {
            return Ok(new ConnectivityResult
            {
                Success = false,
                Message = "Mailbox Email / UPN is not configured. Please enter the mailbox address the application will send as (e.g. inbox@contoso.com) on the Integration Settings page."
            });
        }

        // Resolve recipient: prefer the caller-supplied address, fall back to saved NotificationEmail.
        var recipient = string.IsNullOrWhiteSpace(to) ? dto.NotificationEmail : to.Trim();

        if (string.IsNullOrWhiteSpace(recipient))
        {
            return Ok(new ConnectivityResult
            {
                Success = false,
                Message = "No recipient address supplied. Enter a test email address in the 'Test recipient email' field (or save a Notification Email on the Agent Configuration page)."
            });
        }

        // ── Attempt to send ───────────────────────────────────────────────────
        try
        {
            var testRef = "AGNT-" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
            await mailService.SendEmailAsync(new Models.SendEmailRequest
            {
                To      = recipient,
                Subject = $"[AgentifFlow] Mail Delivery Test [Ref: {testRef}]",
                Body    =
                    $"This is an automated delivery test from AgentifFlow.\n\n" +
                    $"If you receive this message, the mail service is working correctly.\n\n" +
                    $"Sent from mailbox: {dto.GraphMailboxAddress}\n" +
                    $"Sent to:          {recipient}\n" +
                    $"Reference:        {testRef}\n" +
                    $"Timestamp (UTC):  {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}",
                IsHtml  = false
            });

            return Ok(new ConnectivityResult
            {
                Success = true,
                Message =
                    $"Test email successfully submitted to Graph API.\n" +
                    $"Sent from: {dto.GraphMailboxAddress}\n" +
                    $"Sent to:   {recipient}\n" +
                    $"Reference: {testRef}\n\n" +
                    "Please check your inbox (and spam folder). " +
                    "If the email does not arrive within a few minutes, verify that the " +
                    "application has Mail.Send and Mail.ReadWrite permissions consented in " +
                    "Azure AD, and that the Mailbox Address matches a real mailbox."
            });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Mail test send failed");
            return Ok(new ConnectivityResult
            {
                Success = false,
                Message =
                    $"Mail send failed: {ex.Message}\n\n" +
                    "Common causes:\n" +
                    "  • Mail.Send or Mail.ReadWrite application permission not granted / consented in Azure AD\n" +
                    "  • Mailbox Email / UPN does not match a real mailbox in this tenant\n" +
                    "  • Client Secret has expired or is incorrect\n" +
                    "  • Tenant ID / Client ID mismatch"
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
