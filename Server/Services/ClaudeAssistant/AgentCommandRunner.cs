using Microsoft.AspNetCore.SignalR;
using Remotely.Server.Hubs;
using Remotely.Shared;
using Remotely.Shared.Entities;
using Remotely.Shared.Enums;
using Remotely.Shared.Interfaces;
using Remotely.Shared.Utilities;

namespace Remotely.Server.Services.ClaudeAssistant;

public interface IAgentCommandRunner
{
    /// <summary>
    /// Runs a command on an online device through its agent and waits for the result.
    /// The result is stored in ScriptResults like any other command, so it shows up
    /// in the device's script history.
    /// </summary>
    Task<Result<ScriptResult>> Run(
        string organizationId,
        string deviceId,
        ScriptingShell shell,
        string command,
        string senderName,
        CancellationToken cancellationToken);
}

public class AgentCommandRunner : IAgentCommandRunner
{
    private static readonly TimeSpan _resultTimeout = TimeSpan.FromMinutes(3);

    private readonly IHubContext<AgentHub, IAgentHubClient> _agentHubContext;
    private readonly IAgentHubSessionCache _agentSessionCache;
    private readonly IDataService _dataService;
    private readonly IExpiringTokenService _expiringTokenService;

    public AgentCommandRunner(
        IHubContext<AgentHub, IAgentHubClient> agentHubContext,
        IAgentHubSessionCache agentSessionCache,
        IDataService dataService,
        IExpiringTokenService expiringTokenService)
    {
        _agentHubContext = agentHubContext;
        _agentSessionCache = agentSessionCache;
        _dataService = dataService;
        _expiringTokenService = expiringTokenService;
    }

    public async Task<Result<ScriptResult>> Run(
        string organizationId,
        string deviceId,
        ScriptingShell shell,
        string command,
        string senderName,
        CancellationToken cancellationToken)
    {
        if (!_agentSessionCache.TryGetByDeviceId(deviceId, out var device) ||
            !_agentSessionCache.TryGetConnectionId(deviceId, out var connectionId))
        {
            return Result.Fail<ScriptResult>("Device is offline.");
        }

        if (device.OrganizationID != organizationId)
        {
            return Result.Fail<ScriptResult>("Device belongs to another organization.");
        }

        var requestId = Guid.NewGuid().ToString();
        var authToken = _expiringTokenService.GetToken(
            Time.Now.AddMinutes(AppConstants.ScriptRunExpirationMinutes),
            device.OrganizationID,
            device.ID);

        await _agentHubContext.Clients.Client(connectionId).ExecuteCommandFromApi(
            shell,
            authToken,
            requestId,
            command,
            senderName);

        var deadline = DateTimeOffset.Now + _resultTimeout;
        object? commandId = null;
        while (DateTimeOffset.Now < deadline &&
            !AgentHub.ApiScriptResults.TryGetValue(requestId, out commandId))
        {
            await Task.Delay(250, cancellationToken);
        }

        if (commandId is null)
        {
            return Result.Fail<ScriptResult>(
                $"No result within {_resultTimeout.TotalMinutes} minutes.  The command may still be running.");
        }

        AgentHub.ApiScriptResults.Remove(requestId);
        return await _dataService.GetScriptResult($"{commandId}", organizationId);
    }
}
