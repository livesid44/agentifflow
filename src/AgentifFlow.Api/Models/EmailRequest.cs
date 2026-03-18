using System.ComponentModel.DataAnnotations;

namespace AgentifFlow.Api.Models;

public class SendEmailRequest
{
    [Required]
    [EmailAddress]
    public string To { get; set; } = string.Empty;

    [MaxLength(500)]
    public string? Cc { get; set; }

    [Required]
    [MaxLength(500)]
    public string Subject { get; set; } = string.Empty;

    [Required]
    public string Body { get; set; } = string.Empty;

    public bool IsHtml { get; set; } = false;
}

public class EmailMessage
{
    public string Id { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;
    public string From { get; set; } = string.Empty;
    public string BodyPreview { get; set; } = string.Empty;
    public DateTime ReceivedAt { get; set; }
    public bool IsRead { get; set; }
    public string? ConversationId { get; set; }
}
