using AgentifFlow.Api.Models;
using Microsoft.Graph;
using Microsoft.Graph.Users.Item.SendMail;
using Microsoft.Graph.Models;

namespace AgentifFlow.Api.Services;

public class GraphMailService : IGraphMailService
{
    private readonly GraphServiceClient _graphClient;
    private readonly IAppConfigurationService _configService;
    private readonly ILogger<GraphMailService> _logger;

    public GraphMailService(
        GraphServiceClient graphClient,
        IAppConfigurationService configService,
        ILogger<GraphMailService> logger)
    {
        _graphClient = graphClient;
        _configService = configService;
        _logger = logger;
    }

    /// <summary>
    /// Returns a builder scoped to the configured mailbox.
    /// When a <c>GraphMailboxAddress</c> (UPN / email) is stored in the database the service
    /// uses the <c>/users/{address}</c> endpoint, which is required for application-level
    /// (client-credentials) access.  Without it we fall back to <c>/me</c>, which requires a
    /// delegated token.
    /// </summary>
    private async Task<Microsoft.Graph.Users.Item.UserItemRequestBuilder?> GetMailboxBuilderAsync()
    {
        var (_, _, _, mailboxAddress) = await _configService.GetGraphRawSettingsAsync();
        if (!string.IsNullOrWhiteSpace(mailboxAddress))
            return _graphClient.Users[mailboxAddress];
        return null;
    }

    public async Task<IEnumerable<EmailMessage>> GetInboxMessagesAsync(int top = 10)
    {
        _logger.LogInformation("Fetching {Top} inbox messages from Graph API", top);

        var mailbox = await GetMailboxBuilderAsync();

        if (mailbox is not null)
        {
            var messages = await mailbox.MailFolders["inbox"].Messages.GetAsync(config =>
            {
                config.QueryParameters.Top = top;
                config.QueryParameters.Select = ["id", "subject", "from", "bodyPreview", "receivedDateTime", "isRead", "conversationId"];
                config.QueryParameters.Orderby = ["receivedDateTime DESC"];
            });
            return MapMessages(messages?.Value);
        }
        else
        {
            var messages = await _graphClient.Me.MailFolders["inbox"].Messages.GetAsync(config =>
            {
                config.QueryParameters.Top = top;
                config.QueryParameters.Select = ["id", "subject", "from", "bodyPreview", "receivedDateTime", "isRead", "conversationId"];
                config.QueryParameters.Orderby = ["receivedDateTime DESC"];
            });
            return MapMessages(messages?.Value);
        }
    }

    public async Task SendEmailAsync(SendEmailRequest request)
    {
        _logger.LogInformation("Sending email to {To} via Graph API", request.To);

        var recipients = new List<Recipient>
        {
            new Recipient { EmailAddress = new EmailAddress { Address = request.To } }
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
            CcRecipients = ccRecipients.Any() ? ccRecipients : null
        };

        var mailbox = await GetMailboxBuilderAsync();

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
            await _graphClient.Me.SendMail.PostAsync(new Microsoft.Graph.Me.SendMail.SendMailPostRequestBody
            {
                Message = message,
                SaveToSentItems = true
            });
        }
    }

    public async Task<EmailMessage?> GetMessageByIdAsync(string messageId)
    {
        _logger.LogInformation("Getting email message {MessageId}", messageId);

        var mailbox = await GetMailboxBuilderAsync();

        Microsoft.Graph.Models.Message? message;
        if (mailbox is not null)
            message = await mailbox.Messages[messageId].GetAsync(config =>
            {
                config.QueryParameters.Select = ["id", "subject", "from", "bodyPreview", "receivedDateTime", "isRead"];
            });
        else
            message = await _graphClient.Me.Messages[messageId].GetAsync(config =>
            {
                config.QueryParameters.Select = ["id", "subject", "from", "bodyPreview", "receivedDateTime", "isRead"];
            });

        if (message is null) return null;

        return new EmailMessage
        {
            Id = message.Id ?? string.Empty,
            Subject = message.Subject ?? "(No Subject)",
            From = message.From?.EmailAddress?.Address ?? string.Empty,
            BodyPreview = message.BodyPreview ?? string.Empty,
            ReceivedAt = message.ReceivedDateTime?.UtcDateTime ?? DateTime.UtcNow,
            IsRead = message.IsRead ?? false
        };
    }

    public async Task DeleteMessageAsync(string messageId)
    {
        _logger.LogInformation("Deleting email message {MessageId}", messageId);

        var mailbox = await GetMailboxBuilderAsync();
        if (mailbox is not null)
            await mailbox.Messages[messageId].DeleteAsync();
        else
            await _graphClient.Me.Messages[messageId].DeleteAsync();
    }

    public async Task MarkAsReadAsync(string messageId)
    {
        _logger.LogInformation("Marking email message {MessageId} as read", messageId);

        var patch = new Microsoft.Graph.Models.Message { IsRead = true };
        var mailbox = await GetMailboxBuilderAsync();

        if (mailbox is not null)
            await mailbox.Messages[messageId].PatchAsync(patch);
        else
            await _graphClient.Me.Messages[messageId].PatchAsync(patch);
    }

    public async Task ReplyToMessageAsync(string messageId, string replyBody)
    {
        _logger.LogInformation("Replying to email message {MessageId}", messageId);

        var requestBody = new Microsoft.Graph.Users.Item.Messages.Item.Reply.ReplyPostRequestBody
        {
            Comment = replyBody
        };

        var mailbox = await GetMailboxBuilderAsync();

        if (mailbox is not null)
            await mailbox.Messages[messageId].Reply.PostAsync(requestBody);
        else
            await _graphClient.Me.Messages[messageId].Reply.PostAsync(
                new Microsoft.Graph.Me.Messages.Item.Reply.ReplyPostRequestBody
                {
                    Comment = replyBody
                });
    }

    // ── helpers ───────────────────────────────────────────────────────────────

    private static IEnumerable<EmailMessage> MapMessages(IEnumerable<Microsoft.Graph.Models.Message>? messages) =>
        messages?.Select(m => new EmailMessage
        {
            Id = m.Id ?? string.Empty,
            Subject = m.Subject ?? "(No Subject)",
            From = m.From?.EmailAddress?.Address ?? string.Empty,
            BodyPreview = m.BodyPreview ?? string.Empty,
            ReceivedAt = m.ReceivedDateTime?.UtcDateTime ?? DateTime.UtcNow,
            IsRead = m.IsRead ?? false,
            ConversationId = m.ConversationId
        }) ?? Enumerable.Empty<EmailMessage>();
}
