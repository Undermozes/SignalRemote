using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Remotely.Server.Data;
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

    private readonly IAppDbFactory _appDbFactory;
    private readonly IHubContext<AgentHub, IAgentHubClient> _agentHubContext;
    private readonly IAgentHubSessionCache _agentSessionCache;
    private readonly IDataService _dataService;
    private readonly IExpiringTokenService _expiringTokenService;

    public AgentCommandRunner(
        IHubContext<AgentHub, IAgentHubClient> agentHubContext,
        IAppDbFactory appDbFactory,
        IAgentHubSessionCache agentSessionCache,
        IDataService dataService,
        IExpiringTokenService expiringTokenService)
    {
        _agentHubContext = agentHubContext;
        _appDbFactory = appDbFactory;
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

        // Results of this exact command from this sender that already exist, so the
        // fallback below only picks up the new one.
        var existingResultIds = await FindResultIds(organizationId, deviceId, command, senderName, null, cancellationToken);

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
        var nextFallbackCheck = DateTimeOffset.Now + TimeSpan.FromSeconds(1);
        while (DateTimeOffset.Now < deadline)
        {
            if (AgentHub.ApiScriptResults.TryGetValue(requestId, out var commandId))
            {
                AgentHub.ApiScriptResults.Remove(requestId);
                var result = await _dataService.GetScriptResult($"{commandId}", organizationId);
                // Only accept a result the target device produced.
                if (result.IsSuccess && !string.Equals(result.Value.DeviceID, deviceId, StringComparison.OrdinalIgnoreCase))
                {
                    return Result.Fail<ScriptResult>("The agent reported a result from another device.");
                }
                return result;
            }

            // Agents released so far call ScriptResultViaApi(requestId) with one argument,
            // which doesn't bind to the hub method, so the request ID never arrives.  The
            // agent has already stored the result through the API, so find it there.
            if (DateTimeOffset.Now >= nextFallbackCheck)
            {
                nextFallbackCheck = DateTimeOffset.Now + TimeSpan.FromSeconds(1);
                var newIds = await FindResultIds(organizationId, deviceId, command, senderName, existingResultIds, cancellationToken);
                if (newIds.Count > 0)
                {
                    return await _dataService.GetScriptResult(newIds[0], organizationId);
                }
            }

            await Task.Delay(250, cancellationToken);
        }

        return Result.Fail<ScriptResult>(
            $"No result within {_resultTimeout.TotalMinutes} minutes.  The command may still be running.");
    }

    private async Task<List<string>> FindResultIds(
        string organizationId,
        string deviceId,
        string command,
        string senderName,
        IReadOnlyCollection<string>? exclude,
        CancellationToken cancellationToken)
    {
        using var dbContext = _appDbFactory.GetContext();
        var ids = await dbContext.ScriptResults
            .AsNoTracking()
            .Where(x =>
                x.OrganizationID == organizationId &&
                x.DeviceID == deviceId &&
                x.InputType == ScriptInputType.Api &&
                x.SenderUserName == senderName &&
                x.ScriptInput == command)
            .Select(x => x.ID)
            .ToListAsync(cancellationToken);

        return exclude is null ? ids : ids.Where(x => !exclude.Contains(x)).ToList();
    }
}
