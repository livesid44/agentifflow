using AgentifFlow.Api.Models;

namespace AgentifFlow.Api.Services;

public interface IGraphMailService
{
    Task<IEnumerable<EmailMessage>> GetInboxMessagesAsync(int top = 10);
    Task SendEmailAsync(SendEmailRequest request);
    Task<EmailMessage?> GetMessageByIdAsync(string messageId);
    Task DeleteMessageAsync(string messageId);
    Task MarkAsReadAsync(string messageId);
    Task ReplyToMessageAsync(string messageId, string replyBody);
}
