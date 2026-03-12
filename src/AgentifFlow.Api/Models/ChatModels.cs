using System.ComponentModel.DataAnnotations;

namespace AgentifFlow.Api.Models;

public class ChatRequest
{
    [Required]
    [MaxLength(8000)]
    public string Message { get; set; } = string.Empty;

    [MaxLength(500)]
    public string? SystemPrompt { get; set; }

    public IList<ChatHistoryItem>? History { get; set; }

    [Range(1, 4096)]
    public int MaxTokens { get; set; } = 1024;

    [Range(0.0, 2.0)]
    public float Temperature { get; set; } = 0.7f;
}

public class ChatHistoryItem
{
    [Required]
    public string Role { get; set; } = "user";

    [Required]
    public string Content { get; set; } = string.Empty;
}

public class ChatResponse
{
    public string Message { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public int PromptTokens { get; set; }
    public int CompletionTokens { get; set; }
    public int TotalTokens { get; set; }
}
