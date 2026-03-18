using AgentifFlow.Api.Models;

namespace AgentifFlow.Api.Services;

public interface ILlmService
{
    Task<ChatResponse> ChatAsync(ChatRequest request);
    Task<string> SummarizeAsync(string text);
    Task<string> ExtractTasksAsync(string text);
}
