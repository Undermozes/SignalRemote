using Anthropic;
using System.Net.Sockets;
using Remotely.Shared;
using Remotely.Shared.Entities;

namespace Remotely.Server.Services.ClaudeAssistant;

public interface IClaudeAssistantService
{
    Task<bool> IsEnabled();
    /// <summary>
    /// Returns the technician's running session for this device, or starts one.  Sessions live
    /// on the server, so a refreshed or reconnected page continues where it was.
    /// </summary>
    /// <param name="continueSessionId">An earlier session from the history to continue.</param>
    /// <param name="startNew">End the running session and start a new conversation.</param>
    Task<Result<ClaudeAssistantSession>> StartSession(RemotelyUser user, string deviceId, Guid? continueSessionId = null, bool startNew = false);
}

public class ClaudeAssistantService : IClaudeAssistantService
{
    /// <summary>
    /// One HTTP client for all sessions.  TCP keepalive every 30 seconds keeps long Claude
    /// calls alive through NAT routers (the server sits behind a 4G/5G modem) that drop
    /// connections without traffic.
    /// </summary>
    private static readonly HttpClient _httpClient = new(new SocketsHttpHandler
    {
        PooledConnectionLifetime = TimeSpan.FromMinutes(10),
        ConnectCallback = async (context, cancellationToken) =>
        {
            var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
            try
            {
                socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true);
                socket.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveTime, 30);
                socket.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveInterval, 10);
                socket.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveRetryCount, 3);
                await socket.ConnectAsync(context.DnsEndPoint, cancellationToken);
                return new NetworkStream(socket, ownsSocket: true);
            }
            catch
            {
                socket.Dispose();
                throw;
            }
        },
    })
    {
        // The SDK applies its own timeout; the session also stops a stream that stalls.
        Timeout = System.Threading.Timeout.InfiniteTimeSpan,
    };

    private readonly IAgentCommandRunner _commandRunner;
    private readonly IAgentHubSessionCache _agentSessionCache;
    private readonly IClaudeAssistantHistory _history;
    private readonly IClaudeAssistantSessionRegistry _registry;
    private readonly IDataService _dataService;
    private readonly ILoggerFactory _loggerFactory;

    public ClaudeAssistantService(
        IAgentCommandRunner commandRunner,
        IAgentHubSessionCache agentSessionCache,
        IClaudeAssistantHistory history,
        IClaudeAssistantSessionRegistry registry,
        IDataService dataService,
        ILoggerFactory loggerFactory)
    {
        _commandRunner = commandRunner;
        _agentSessionCache = agentSessionCache;
        _history = history;
        _registry = registry;
        _dataService = dataService;
        _loggerFactory = loggerFactory;
    }

    public async Task<bool> IsEnabled()
    {
        var settings = await _dataService.GetSettings();
        return settings.ClaudeAssistantEnabled;
    }

    public async Task<Result<ClaudeAssistantSession>> StartSession(RemotelyUser user, string deviceId, Guid? continueSessionId = null, bool startNew = false)
    {
        var settings = await _dataService.GetSettings();
        if (!settings.ClaudeAssistantEnabled)
        {
            return Result.Fail<ClaudeAssistantSession>("The Claude assistant is disabled in Server Config.");
        }

        if (!_dataService.DoesUserHaveAccessToDevice(deviceId, user))
        {
            return Result.Fail<ClaudeAssistantSession>("You don't have access to this device.");
        }

        var deviceResult = await _dataService.GetDevice(user.OrganizationID, deviceId);
        if (!deviceResult.IsSuccess)
        {
            return Result.Fail<ClaudeAssistantSession>("Device not found.");
        }

        var apiKey = string.IsNullOrWhiteSpace(settings.ClaudeApiKey)
            ? Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY")
            : settings.ClaudeApiKey;

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return Result.Fail<ClaudeAssistantSession>(
                "No Anthropic API key configured.  Set it in Server Config or the ANTHROPIC_API_KEY environment variable.");
        }

        // Pick up a session that is still running on the server.
        if (continueSessionId is Guid runningId && _registry.TryGet(runningId, out var running))
        {
            if (running.Device.ID != deviceId)
            {
                return Result.Fail<ClaudeAssistantSession>("That conversation was about another device.");
            }
            if (running.User.UserName != user.UserName)
            {
                return Result.Fail<ClaudeAssistantSession>($"{running.User.UserName} is using that conversation right now.");
            }
            return Result.Ok(running);
        }

        if (continueSessionId is null && _registry.TryGetActive($"{user.UserName}", deviceId, out var active))
        {
            if (!startNew)
            {
                return Result.Ok(active);
            }
            _registry.End(active);
        }

        ResumedConversation? resumedFrom = null;
        if (continueSessionId is Guid sessionId)
        {
            // Same visibility as the history page: admins see the organization, others their own sessions.
            var events = await _history.GetSessionEvents(
                user.OrganizationID,
                user.IsAdministrator ? null : user.UserName,
                sessionId);

            if (events.Count == 0)
            {
                return Result.Fail<ClaudeAssistantSession>("That conversation wasn't found in the history.");
            }

            if (events.Any(x => x.DeviceId != deviceId))
            {
                return Result.Fail<ClaudeAssistantSession>("That conversation was about another device.");
            }

            resumedFrom = ClaudeConversationRestorer.Restore(sessionId, events);
        }

        var client = new AnthropicClient
        {
            ApiKey = apiKey,
            HttpClient = _httpClient,
            // Long agent turns stream for minutes; stalls are caught by the session itself.
            Timeout = TimeSpan.FromMinutes(20),
        };
        var model = string.IsNullOrWhiteSpace(settings.ClaudeModel) ? "claude-opus-5-5" : settings.ClaudeModel.Trim();

        var session = new ClaudeAssistantSession(
            client,
            _commandRunner,
            _agentSessionCache,
            _history,
            new ClaudePricing(
                settings.ClaudePriceInputPerMTok,
                settings.ClaudePriceOutputPerMTok,
                settings.ClaudePriceCacheReadPerMTok,
                settings.ClaudePriceCacheWritePerMTok),
            _loggerFactory.CreateLogger<ClaudeAssistantSession>(),
            user,
            deviceResult.Value,
            model,
            settings.ClaudeAutoRunReadOnlyCommands,
            resumedFrom);

        _registry.Add(session);
        return Result.Ok(session);
    }
}
