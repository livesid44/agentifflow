using AgentifFlow.Api.Models;
using Microsoft.Graph;
using Microsoft.Graph.Me.SendMail;
using Microsoft.Graph.Models;

namespace AgentifFlow.Api.Services;

public class GraphMailService : IGraphMailService
{
    private readonly GraphServiceClient _graphClient;
    private readonly ILogger<GraphMailService> _logger;

    public GraphMailService(GraphServiceClient graphClient, ILogger<GraphMailService> logger)
    {
        _graphClient = graphClient;
        _logger = logger;
    }

    public async Task<IEnumerable<EmailMessage>> GetInboxMessagesAsync(int top = 10)
    {
        _logger.LogInformation("Fetching {Top} inbox messages from Graph API", top);

        var messages = await _graphClient.Me.MailFolders["inbox"].Messages.GetAsync(config =>
        {
            config.QueryParameters.Top = top;
            config.QueryParameters.Select = ["id", "subject", "from", "bodyPreview", "receivedDateTime", "isRead"];
            config.QueryParameters.Orderby = ["receivedDateTime DESC"];
        });

        return messages?.Value?.Select(m => new EmailMessage
        {
            Id = m.Id ?? string.Empty,
            Subject = m.Subject ?? "(No Subject)",
            From = m.From?.EmailAddress?.Address ?? string.Empty,
            BodyPreview = m.BodyPreview ?? string.Empty,
            ReceivedAt = m.ReceivedDateTime?.UtcDateTime ?? DateTime.UtcNow,
            IsRead = m.IsRead ?? false
        }) ?? Enumerable.Empty<EmailMessage>();
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
            {
                ccRecipients.Add(new Recipient { EmailAddress = new EmailAddress { Address = cc.Trim() } });
            }
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

        await _graphClient.Me.SendMail.PostAsync(new SendMailPostRequestBody
        {
            Message = message,
            SaveToSentItems = true
        });
    }

    public async Task<EmailMessage?> GetMessageByIdAsync(string messageId)
    {
        _logger.LogInformation("Getting email message {MessageId}", messageId);

        var message = await _graphClient.Me.Messages[messageId].GetAsync(config =>
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
        await _graphClient.Me.Messages[messageId].DeleteAsync();
    }
}
