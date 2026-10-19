using Anthropic;
using Remotely.Shared;
using Remotely.Shared.Entities;

namespace Remotely.Server.Services.ClaudeAssistant;

public interface IClaudeAssistantService
{
    Task<bool> IsEnabled();
    /// <param name="continueSessionId">An earlier session from the history to continue, or null for a new one.</param>
    Task<Result<ClaudeAssistantSession>> StartSession(RemotelyUser user, string deviceId, Guid? continueSessionId = null);
}

public class ClaudeAssistantService : IClaudeAssistantService
{
    private readonly IAgentCommandRunner _commandRunner;
    private readonly IAgentHubSessionCache _agentSessionCache;
    private readonly IClaudeAssistantHistory _history;
    private readonly IDataService _dataService;
    private readonly ILoggerFactory _loggerFactory;

    public ClaudeAssistantService(
        IAgentCommandRunner commandRunner,
        IAgentHubSessionCache agentSessionCache,
        IClaudeAssistantHistory history,
        IDataService dataService,
        ILoggerFactory loggerFactory)
    {
        _commandRunner = commandRunner;
        _agentSessionCache = agentSessionCache;
        _history = history;
        _dataService = dataService;
        _loggerFactory = loggerFactory;
    }

    public async Task<bool> IsEnabled()
    {
        var settings = await _dataService.GetSettings();
        return settings.ClaudeAssistantEnabled;
    }

    public async Task<Result<ClaudeAssistantSession>> StartSession(RemotelyUser user, string deviceId, Guid? continueSessionId = null)
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

        var client = new AnthropicClient { ApiKey = apiKey };
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

        return Result.Ok(session);
    }
}
