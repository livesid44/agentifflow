using Microsoft.AspNetCore.SignalR;

namespace AgentifFlow.Api.Hubs;

/// <summary>
/// SignalR hub that pushes real-time agent job notifications to connected Blazor clients.
/// All communication is server → client only; clients cannot invoke hub methods.
/// </summary>
public class AgentNotificationHub : Hub
{
    // Client-callable methods are intentionally absent — the hub is push-only.
    // The server broadcasts via IAgentNotificationService → IHubContext<AgentNotificationHub>.
}
