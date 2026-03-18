using AgentifFlow.Api.Hubs;
using AgentifFlow.Api.Models;
using Microsoft.AspNetCore.SignalR;

namespace AgentifFlow.Api.Services;

/// <summary>
/// Uses <see cref="IHubContext{AgentNotificationHub}"/> to broadcast real-time job events
/// to all connected Blazor dashboard clients.
/// </summary>
public class AgentNotificationService : IAgentNotificationService
{
    private readonly IHubContext<AgentNotificationHub> _hub;
    private readonly ILogger<AgentNotificationService> _logger;

    public AgentNotificationService(
        IHubContext<AgentNotificationHub> hub,
        ILogger<AgentNotificationService> logger)
    {
        _hub    = hub;
        _logger = logger;
    }

    public async Task NotifyJobCreatedAsync(BlobWatcherJobDto dto)
    {
        try
        {
            await _hub.Clients.All.SendAsync("JobCreated", dto);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to broadcast JobCreated for job {JobId}.", dto.Id);
        }
    }

    public async Task NotifyJobUpdatedAsync(BlobWatcherJobDto dto)
    {
        try
        {
            await _hub.Clients.All.SendAsync("JobUpdated", dto);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to broadcast JobUpdated for job {JobId}.", dto.Id);
        }
    }
}
