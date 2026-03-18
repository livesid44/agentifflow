using AgentifFlow.Api.Models;
using Azure.AI.OpenAI;
using OpenAI.Chat;

namespace AgentifFlow.Api.Services;

public class LlmService : ILlmService
{
    private readonly IAppConfigurationService _configService;
    private readonly ILogger<LlmService> _logger;

    public LlmService(IAppConfigurationService configService, ILogger<LlmService> logger)
    {
        _configService = configService;
        _logger = logger;
    }

    /// <summary>
    /// Resolves the Azure OpenAI client and deployment name from the database-stored
    /// configuration. Throws <see cref="InvalidOperationException"/> when the settings
    /// have not yet been saved through the Integration Settings page.
    /// </summary>
    private async Task<(AzureOpenAIClient Client, string DeploymentName)> ResolveClientAsync()
    {
        var (endpoint, apiKey, deploymentName) = await _configService.GetOpenAiRawSettingsAsync();

        if (string.IsNullOrWhiteSpace(endpoint) || string.IsNullOrWhiteSpace(apiKey))
            throw new InvalidOperationException(
                "Azure OpenAI is not configured. Please enter the endpoint and API key on the Integration Settings page.");

        var client = new AzureOpenAIClient(
            new Uri(endpoint),
            new System.ClientModel.ApiKeyCredential(apiKey));

        return (client, string.IsNullOrWhiteSpace(deploymentName) ? "gpt-4o" : deploymentName);
    }

    public async Task<ChatResponse> ChatAsync(ChatRequest request)
    {
        _logger.LogInformation("Sending chat request to Azure OpenAI");

        var (client, deploymentName) = await ResolveClientAsync();
        var chatClient = client.GetChatClient(deploymentName);

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
            Model = deploymentName,
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
