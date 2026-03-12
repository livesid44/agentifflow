using AgentifFlow.Api.Models;
using Azure.AI.OpenAI;
using OpenAI.Chat;

namespace AgentifFlow.Api.Services;

public class LlmService : ILlmService
{
    private readonly AzureOpenAIClient _openAiClient;
    private readonly IConfiguration _configuration;
    private readonly ILogger<LlmService> _logger;

    public LlmService(AzureOpenAIClient openAiClient, IConfiguration configuration, ILogger<LlmService> logger)
    {
        _openAiClient = openAiClient;
        _configuration = configuration;
        _logger = logger;
    }

    private string DeploymentName => _configuration["AzureOpenAI:DeploymentName"] ?? "gpt-4o";

    public async Task<ChatResponse> ChatAsync(ChatRequest request)
    {
        _logger.LogInformation("Sending chat request to Azure OpenAI");

        var chatClient = _openAiClient.GetChatClient(DeploymentName);

        var messages = new List<ChatMessage>();

        var systemPrompt = request.SystemPrompt
            ?? "You are AgentifFlow, a helpful AI assistant that helps users manage tasks, emails, and workflows.";
        messages.Add(new SystemChatMessage(systemPrompt));

        if (request.History != null)
        {
            foreach (var item in request.History)
            {
                messages.Add(item.Role.Equals("assistant", StringComparison.OrdinalIgnoreCase)
                    ? new AssistantChatMessage(item.Content)
                    : new UserChatMessage(item.Content));
            }
        }

        messages.Add(new UserChatMessage(request.Message));

        var options = new ChatCompletionOptions
        {
            MaxOutputTokenCount = request.MaxTokens,
            Temperature = request.Temperature
        };

        var completion = await chatClient.CompleteChatAsync(messages, options);

        var result = completion.Value;
        return new ChatResponse
        {
            Message = result.Content[0].Text,
            Model = DeploymentName,
            PromptTokens = result.Usage.InputTokenCount,
            CompletionTokens = result.Usage.OutputTokenCount,
            TotalTokens = result.Usage.TotalTokenCount
        };
    }

    public async Task<string> SummarizeAsync(string text)
    {
        _logger.LogInformation("Summarizing text via Azure OpenAI");

        var request = new ChatRequest
        {
            Message = $"Please summarize the following text concisely:\n\n{text}",
            SystemPrompt = "You are a helpful assistant that creates concise and accurate summaries.",
            MaxTokens = 512
        };

        var response = await ChatAsync(request);
        return response.Message;
    }

    public async Task<string> ExtractTasksAsync(string text)
    {
        _logger.LogInformation("Extracting tasks from text via Azure OpenAI");

        var request = new ChatRequest
        {
            Message = $"Extract all actionable tasks from the following text. Return a JSON array of objects with 'title' and 'description' fields:\n\n{text}",
            SystemPrompt = "You are a task extraction assistant. You extract actionable tasks from text and return structured JSON. Only return valid JSON, no additional text.",
            MaxTokens = 1024
        };

        var response = await ChatAsync(request);
        return response.Message;
    }
}
