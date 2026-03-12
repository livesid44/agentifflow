using AgentifFlow.Api.Models;

namespace AgentifFlow.Api.Services;

/// <summary>
/// Broadcasts real-time agent job status events to all connected SignalR clients.
/// </summary>
public interface IAgentNotificationService
{
    /// <summary>Broadcasts a "new job created" event to all hub clients.</summary>
    Task NotifyJobCreatedAsync(BlobWatcherJobDto dto);

    /// <summary>Broadcasts a "job status updated" event to all hub clients.</summary>
    Task NotifyJobUpdatedAsync(BlobWatcherJobDto dto);
}
