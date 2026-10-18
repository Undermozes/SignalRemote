using Microsoft.AspNetCore.SignalR;
using Remotely.Shared.Entities;

namespace Remotely.Server.Hubs;

/// <summary>
/// Blocks every <see cref="AgentHub"/> method until the connection has registered
/// (and authenticated) as a device via <see cref="AgentHub.DeviceCameOnline"/>.
/// Without this, an anonymous connection could relay messages to arbitrary
/// browser or viewer connections.
/// </summary>
public class AgentHubAuthFilter : IHubFilter
{
    private static readonly HashSet<string> _anonymousMethods = new(StringComparer.Ordinal)
    {
        nameof(AgentHub.DeviceCameOnline),
        nameof(AgentHub.GetServerUrl),
    };

    private readonly ILogger<AgentHubAuthFilter> _logger;

    public AgentHubAuthFilter(ILogger<AgentHubAuthFilter> logger)
    {
        _logger = logger;
    }

    public async ValueTask<object?> InvokeMethodAsync(
        HubInvocationContext invocationContext,
        Func<HubInvocationContext, ValueTask<object?>> next)
    {
        if (invocationContext.Hub is AgentHub &&
            !_anonymousMethods.Contains(invocationContext.HubMethodName) &&
            invocationContext.Context.Items["Device"] is not Device)
        {
            _logger.LogWarning(
                "Rejected call to {method} from unregistered agent connection {connectionId}.",
                invocationContext.HubMethodName,
                invocationContext.Context.ConnectionId);
            throw new HubException("Device is not registered.");
        }

        return await next(invocationContext);
    }
}
