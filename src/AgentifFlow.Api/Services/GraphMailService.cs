using AgentifFlow.Api.Models;
using Azure.Identity;
using Microsoft.Graph;
using Microsoft.Graph.Users.Item.SendMail;
using Microsoft.Graph.Models;

namespace AgentifFlow.Api.Services;

public class GraphMailService : IGraphMailService
{
    private readonly IAppConfigurationService _configService;
    private readonly ILogger<GraphMailService> _logger;

    public GraphMailService(
        IAppConfigurationService configService,
        ILogger<GraphMailService> logger)
    {
        _configService = configService;
        _logger = logger;
    }

    // ── Client factory ────────────────────────────────────────────────────────

    /// <summary>
    /// Builds a <see cref="GraphServiceClient"/> authenticated with the credentials
    /// stored in the database (client-credentials / application flow).
    /// Throws <see cref="InvalidOperationException"/> when the credentials have not
    /// yet been saved on the Integration Settings page.
    /// </summary>
    private async Task<GraphServiceClient> BuildGraphClientAsync()
    {
        var (tenantId, clientId, clientSecret, _) = await _configService.GetGraphRawSettingsAsync();

        if (string.IsNullOrWhiteSpace(tenantId) ||
            string.IsNullOrWhiteSpace(clientId) ||
            string.IsNullOrWhiteSpace(clientSecret))
        {
            throw new InvalidOperationException(
                "Microsoft Graph is not configured. Please save the Tenant ID, Client ID and " +
                "Client Secret on the Integration Settings page.");
        }

        // ClientSecretCredential is part of Azure.Identity (available as a transitive
        // dependency).  It uses the client-credentials flow (application permissions)
        // which is appropriate for background-service scenarios.
        var credential = new ClientSecretCredential(tenantId, clientId, clientSecret);
        return new GraphServiceClient(credential, ["https://graph.microsoft.com/.default"]);
    }

    /// <summary>
    /// Returns a request builder scoped to the configured mailbox address.
    /// The mailbox address (UPN / email) is required for application-level
    /// (client-credentials) access — without it the /users/{id} path is unavailable.
    /// </summary>
    private async Task<(GraphServiceClient Client, Microsoft.Graph.Users.Item.UserItemRequestBuilder? Mailbox)>
        GetClientAndMailboxAsync()
    {
        var client = await BuildGraphClientAsync();
        var (_, _, _, mailboxAddress) = await _configService.GetGraphRawSettingsAsync();
        var mailbox = string.IsNullOrWhiteSpace(mailboxAddress)
            ? null
            : client.Users[mailboxAddress];
        return (client, mailbox);
    }

    // ── IGraphMailService implementation ──────────────────────────────────────

    public async Task<IEnumerable<EmailMessage>> GetInboxMessagesAsync(int top = 10)
    {
        _logger.LogInformation("Fetching {Top} inbox messages from Graph API", top);

        var (client, mailbox) = await GetClientAndMailboxAsync();

        if (mailbox is not null)
        {
            var messages = await mailbox.MailFolders["inbox"].Messages.GetAsync(cfg =>
            {
                cfg.QueryParameters.Top = top;
                cfg.QueryParameters.Select = ["id", "subject", "from", "bodyPreview", "receivedDateTime", "isRead", "conversationId"];
                cfg.QueryParameters.Orderby = ["receivedDateTime DESC"];
            });
            return MapMessages(messages?.Value);
        }
        else
        {
            var messages = await client.Me.MailFolders["inbox"].Messages.GetAsync(cfg =>
            {
                cfg.QueryParameters.Top = top;
                cfg.QueryParameters.Select = ["id", "subject", "from", "bodyPreview", "receivedDateTime", "isRead", "conversationId"];
                cfg.QueryParameters.Orderby = ["receivedDateTime DESC"];
            });
            return MapMessages(messages?.Value);
        }
    }

    public async Task SendEmailAsync(SendEmailRequest request)
    {
        _logger.LogInformation("Sending email to {To} via Graph API", request.To);

        var recipients = new List<Recipient>
        {
            new() { EmailAddress = new EmailAddress { Address = request.To } }
        };

        var ccRecipients = new List<Recipient>();
        if (!string.IsNullOrWhiteSpace(request.Cc))
        {
            foreach (var cc in request.Cc.Split(',', StringSplitOptions.RemoveEmptyEntries))
                ccRecipients.Add(new Recipient { EmailAddress = new EmailAddress { Address = cc.Trim() } });
        }

        var message = new Message
        {
            Subject = request.Subject,
            Body = new ItemBody
            {
                ContentType = request.IsHtml ? BodyType.Html : BodyType.Text,
                Content = request.Body
            },
            ToRecipients = recipients,
            CcRecipients = ccRecipients
        };

        var (client, mailbox) = await GetClientAndMailboxAsync();

        if (mailbox is not null)
        {
            await mailbox.SendMail.PostAsync(new SendMailPostRequestBody
            {
                Message = message,
                SaveToSentItems = true
            });
        }
        else
        {
            await client.Me.SendMail.PostAsync(new Microsoft.Graph.Me.SendMail.SendMailPostRequestBody
            {
                Message = message,
                SaveToSentItems = true
            });
        }

        _logger.LogInformation("Email successfully submitted to Graph API for delivery to {To}", request.To);
    }

    public async Task<EmailMessage?> GetMessageByIdAsync(string messageId)
    {
        _logger.LogInformation("Getting email message {MessageId}", messageId);

        var (client, mailbox) = await GetClientAndMailboxAsync();

        Microsoft.Graph.Models.Message? message;
        if (mailbox is not null)
            message = await mailbox.Messages[messageId].GetAsync(cfg =>
            {
                cfg.QueryParameters.Select = ["id", "subject", "from", "bodyPreview", "receivedDateTime", "isRead"];
            });
        else
            message = await client.Me.Messages[messageId].GetAsync(cfg =>
            {
                cfg.QueryParameters.Select = ["id", "subject", "from", "bodyPreview", "receivedDateTime", "isRead"];
            });

        if (message is null) return null;

        return new EmailMessage
        {
            Id             = message.Id ?? string.Empty,
            Subject        = message.Subject ?? "(No Subject)",
            From           = message.From?.EmailAddress?.Address ?? string.Empty,
            BodyPreview    = message.BodyPreview ?? string.Empty,
            ReceivedAt     = message.ReceivedDateTime?.UtcDateTime ?? DateTime.UtcNow,
            IsRead         = message.IsRead ?? false
        };
    }

    public async Task DeleteMessageAsync(string messageId)
    {
        _logger.LogInformation("Deleting email message {MessageId}", messageId);

        var (client, mailbox) = await GetClientAndMailboxAsync();
        if (mailbox is not null)
            await mailbox.Messages[messageId].DeleteAsync();
        else
            await client.Me.Messages[messageId].DeleteAsync();
    }

    public async Task MarkAsReadAsync(string messageId)
    {
        _logger.LogInformation("Marking email message {MessageId} as read", messageId);

        var patch = new Microsoft.Graph.Models.Message { IsRead = true };
        var (client, mailbox) = await GetClientAndMailboxAsync();

        if (mailbox is not null)
            await mailbox.Messages[messageId].PatchAsync(patch);
        else
            await client.Me.Messages[messageId].PatchAsync(patch);
    }

    public async Task ReplyToMessageAsync(string messageId, string replyBody)
    {
        _logger.LogInformation("Replying to email message {MessageId}", messageId);

        var (client, mailbox) = await GetClientAndMailboxAsync();

        if (mailbox is not null)
        {
            await mailbox.Messages[messageId].Reply.PostAsync(
                new Microsoft.Graph.Users.Item.Messages.Item.Reply.ReplyPostRequestBody
                {
                    Comment = replyBody
                });
        }
        else
        {
            await client.Me.Messages[messageId].Reply.PostAsync(
                new Microsoft.Graph.Me.Messages.Item.Reply.ReplyPostRequestBody
                {
                    Comment = replyBody
                });
        }
    }

    // ── helpers ───────────────────────────────────────────────────────────────

    private static IEnumerable<EmailMessage> MapMessages(IEnumerable<Microsoft.Graph.Models.Message>? messages) =>
        messages?.Select(m => new EmailMessage
        {
            Id             = m.Id ?? string.Empty,
            Subject        = m.Subject ?? "(No Subject)",
            From           = m.From?.EmailAddress?.Address ?? string.Empty,
            BodyPreview    = m.BodyPreview ?? string.Empty,
            ReceivedAt     = m.ReceivedDateTime?.UtcDateTime ?? DateTime.UtcNow,
            IsRead         = m.IsRead ?? false,
            ConversationId = m.ConversationId
        }) ?? Enumerable.Empty<EmailMessage>();
}

