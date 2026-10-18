using Anthropic;
using Remotely.Shared;
using Remotely.Shared.Entities;

namespace Remotely.Server.Services.ClaudeAssistant;

public interface IClaudeAssistantService
{
    Task<bool> IsEnabled();
    Task<Result<ClaudeAssistantSession>> StartSession(RemotelyUser user, string deviceId);
}

public class ClaudeAssistantService : IClaudeAssistantService
{
    private readonly IAgentCommandRunner _commandRunner;
    private readonly IAgentHubSessionCache _agentSessionCache;
    private readonly IDataService _dataService;
    private readonly ILoggerFactory _loggerFactory;

    public ClaudeAssistantService(
        IAgentCommandRunner commandRunner,
        IAgentHubSessionCache agentSessionCache,
        IDataService dataService,
        ILoggerFactory loggerFactory)
    {
        _commandRunner = commandRunner;
        _agentSessionCache = agentSessionCache;
        _dataService = dataService;
        _loggerFactory = loggerFactory;
    }

    public async Task<bool> IsEnabled()
    {
        var settings = await _dataService.GetSettings();
        return settings.ClaudeAssistantEnabled;
    }

    public async Task<Result<ClaudeAssistantSession>> StartSession(RemotelyUser user, string deviceId)
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

        var client = new AnthropicClient { ApiKey = apiKey };
        var model = string.IsNullOrWhiteSpace(settings.ClaudeModel) ? "claude-opus-5-5" : settings.ClaudeModel.Trim();

        var session = new ClaudeAssistantSession(
            client,
            _commandRunner,
            _agentSessionCache,
            _loggerFactory.CreateLogger<ClaudeAssistantSession>(),
            user,
            deviceResult.Value,
            model,
            settings.ClaudeAutoRunReadOnlyCommands);

        return Result.Ok(session);
    }
}
