using Microsoft.EntityFrameworkCore;
using Remotely.Server.Data;
using Remotely.Shared.Entities;

namespace Remotely.Server.Services.ClaudeAssistant;

/// <summary>Prices in USD per million tokens (from Server Config).</summary>
public record ClaudePricing(decimal InputPerMTok, decimal OutputPerMTok, decimal CacheReadPerMTok, decimal CacheWritePerMTok)
{
    public decimal CostOf(long input, long output, long cacheRead, long cacheWrite) =>
        (input * InputPerMTok + output * OutputPerMTok + cacheRead * CacheReadPerMTok + cacheWrite * CacheWritePerMTok)
        / 1_000_000m;
}

/// <summary>
/// Token usage of one Claude API call, stored as JSON on Usage events.  CostUsd is
/// computed with the prices in effect when the call was made.
/// </summary>
public record ClaudeUsage(long Input, long Output, long CacheRead, long CacheWrite, decimal CostUsd = 0)
{
    public long Total => Input + Output + CacheRead + CacheWrite;

    public static ClaudeUsage Parse(string? json)
    {
        try
        {
            return string.IsNullOrWhiteSpace(json)
                ? new(0, 0, 0, 0)
                : System.Text.Json.JsonSerializer.Deserialize<ClaudeUsage>(json) ?? new(0, 0, 0, 0);
        }
        catch (System.Text.Json.JsonException)
        {
            return new(0, 0, 0, 0);
        }
    }

    public static ClaudeUsage Sum(IEnumerable<ClaudeUsage> usages) => usages.Aggregate(
        new ClaudeUsage(0, 0, 0, 0),
        (a, b) => new(a.Input + b.Input, a.Output + b.Output, a.CacheRead + b.CacheRead, a.CacheWrite + b.CacheWrite, a.CostUsd + b.CostUsd));
}

public record ClaudeHistorySessionSummary(
    Guid SessionId,
    DateTimeOffset Started,
    DateTimeOffset LastActivity,
    string? UserName,
    string DeviceId,
    string? DeviceName,
    string? FirstQuestion,
    int Commands,
    int Screenshots,
    int Errors,
    ClaudeUsage Usage);

public record ClaudeHistoryPage(IReadOnlyList<ClaudeHistorySessionSummary> Sessions, int TotalSessions);

/// <summary>
/// Persistent audit history of Claude assistant sessions: every question, answer,
/// command (with approval and output) and screenshot.
/// </summary>
public interface IClaudeAssistantHistory
{
    /// <summary>
    /// Stores an event.  Never throws: a history failure must not break the session.
    /// </summary>
    Task Record(ClaudeAssistantEvent assistantEvent);

    /// <param name="userName">Only sessions of this technician, or null for everyone.</param>
    Task<ClaudeHistoryPage> GetSessions(
        string organizationId,
        string? userName,
        string? search,
        int page,
        int pageSize);

    /// <summary>The session's events in order, without screenshot bytes.</summary>
    Task<IReadOnlyList<ClaudeAssistantEvent>> GetSessionEvents(
        string organizationId,
        string? userName,
        Guid sessionId);

    Task<byte[]?> GetScreenshot(string organizationId, string? userName, Guid eventId);

    /// <summary>All sessions that started in [fromUtc, toUtc), oldest first, e.g. for billing.</summary>
    Task<IReadOnlyList<ClaudeHistorySessionSummary>> GetSessionsInRange(
        string organizationId,
        string? userName,
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc);
}

public class ClaudeAssistantHistory : IClaudeAssistantHistory
{
    private readonly IAppDbFactory _appDbFactory;
    private readonly ILogger<ClaudeAssistantHistory> _logger;

    public ClaudeAssistantHistory(IAppDbFactory appDbFactory, ILogger<ClaudeAssistantHistory> logger)
    {
        _appDbFactory = appDbFactory;
        _logger = logger;
    }

    public async Task Record(ClaudeAssistantEvent assistantEvent)
    {
        try
        {
            using var dbContext = _appDbFactory.GetContext();
            dbContext.ClaudeAssistantEvents.Add(assistantEvent);
            await dbContext.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to store Claude assistant history event {kind} for session {sessionId}.",
                assistantEvent.Kind,
                assistantEvent.SessionId);
        }
    }

    public async Task<ClaudeHistoryPage> GetSessions(
        string organizationId,
        string? userName,
        string? search,
        int page,
        int pageSize)
    {
        using var dbContext = _appDbFactory.GetContext();

        var starts = dbContext.ClaudeAssistantEvents
            .AsNoTracking()
            .Where(x => x.OrganizationId == organizationId && x.Kind == ClaudeAssistantEventKind.SessionStarted);

        if (!string.IsNullOrWhiteSpace(userName))
        {
            starts = starts.Where(x => x.UserName == userName);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            var matchingSessions = dbContext.ClaudeAssistantEvents
                .Where(x => x.OrganizationId == organizationId &&
                    (x.Text != null && x.Text.Contains(term) ||
                     x.DeviceName != null && x.DeviceName.Contains(term) ||
                     x.UserName != null && x.UserName.Contains(term)))
                .Select(x => x.SessionId);
            starts = starts.Where(x => matchingSessions.Contains(x.SessionId));
        }

        var total = await starts.CountAsync();

        var pageStarts = await starts
            .OrderByDescending(x => x.Timestamp)
            .Skip(Math.Max(0, page) * pageSize)
            .Take(pageSize)
            .Select(x => new { x.SessionId, x.Timestamp, x.UserName, x.DeviceId, x.DeviceName })
            .ToListAsync();

        var sessions = await Summarize(
            dbContext,
            pageStarts.Select(x => new SessionStart(x.SessionId, x.Timestamp, x.UserName, x.DeviceId, x.DeviceName)).ToList());

        return new ClaudeHistoryPage(sessions, total);
    }

    public async Task<IReadOnlyList<ClaudeHistorySessionSummary>> GetSessionsInRange(
        string organizationId,
        string? userName,
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc)
    {
        using var dbContext = _appDbFactory.GetContext();

        var starts = dbContext.ClaudeAssistantEvents
            .AsNoTracking()
            .Where(x => x.OrganizationId == organizationId &&
                x.Kind == ClaudeAssistantEventKind.SessionStarted &&
                x.Timestamp >= fromUtc &&
                x.Timestamp < toUtc);

        if (!string.IsNullOrWhiteSpace(userName))
        {
            starts = starts.Where(x => x.UserName == userName);
        }

        var list = await starts
            .OrderBy(x => x.Timestamp)
            .Select(x => new SessionStart(x.SessionId, x.Timestamp, x.UserName, x.DeviceId, x.DeviceName))
            .ToListAsync();

        return await Summarize(dbContext, list);
    }

    private record SessionStart(Guid SessionId, DateTimeOffset Timestamp, string? UserName, string DeviceId, string? DeviceName);

    // Summarize in memory: a page (or a billing period) of sessions is small, and it
    // keeps the query simple for every database provider.
    private static async Task<List<ClaudeHistorySessionSummary>> Summarize(AppDb dbContext, List<SessionStart> starts)
    {
        var sessionIds = starts.Select(x => x.SessionId).ToList();

        var events = await dbContext.ClaudeAssistantEvents
            .AsNoTracking()
            .Where(x => sessionIds.Contains(x.SessionId))
            .Select(x => new { x.SessionId, x.Kind, x.Timestamp, x.Text, x.Output })
            .ToListAsync();

        return starts
            .Select(start =>
            {
                var sessionEvents = events
                    .Where(x => x.SessionId == start.SessionId)
                    .OrderBy(x => x.Timestamp)
                    .ToList();

                return new ClaudeHistorySessionSummary(
                    start.SessionId,
                    start.Timestamp,
                    sessionEvents.Count > 0 ? sessionEvents[^1].Timestamp : start.Timestamp,
                    start.UserName,
                    start.DeviceId,
                    start.DeviceName,
                    sessionEvents.FirstOrDefault(x => x.Kind == ClaudeAssistantEventKind.UserMessage)?.Text,
                    sessionEvents.Count(x => x.Kind == ClaudeAssistantEventKind.CommandRequested),
                    sessionEvents.Count(x => x.Kind == ClaudeAssistantEventKind.Screenshot),
                    sessionEvents.Count(x => x.Kind is ClaudeAssistantEventKind.Error or ClaudeAssistantEventKind.CommandFailed),
                    ClaudeUsage.Sum(sessionEvents
                        .Where(x => x.Kind == ClaudeAssistantEventKind.Usage)
                        .Select(x => ClaudeUsage.Parse(x.Output))));
            })
            .ToList();
    }

    public async Task<IReadOnlyList<ClaudeAssistantEvent>> GetSessionEvents(
        string organizationId,
        string? userName,
        Guid sessionId)
    {
        using var dbContext = _appDbFactory.GetContext();

        var query = dbContext.ClaudeAssistantEvents
            .AsNoTracking()
            .Where(x => x.OrganizationId == organizationId && x.SessionId == sessionId);

        if (!string.IsNullOrWhiteSpace(userName))
        {
            query = query.Where(x => x.UserName == userName);
        }

        var events = await query
            .OrderBy(x => x.Timestamp)
            .Select(x => new ClaudeAssistantEvent
            {
                Id = x.Id,
                SessionId = x.SessionId,
                Timestamp = x.Timestamp,
                Kind = x.Kind,
                OrganizationId = x.OrganizationId,
                DeviceId = x.DeviceId,
                DeviceName = x.DeviceName,
                UserName = x.UserName,
                Text = x.Text,
                Shell = x.Shell,
                Purpose = x.Purpose,
                IsReadOnly = x.IsReadOnly,
                Output = x.Output,
                DecidedBy = x.DecidedBy,
            })
            .ToListAsync();

        return events;
    }

    public async Task<byte[]?> GetScreenshot(string organizationId, string? userName, Guid eventId)
    {
        using var dbContext = _appDbFactory.GetContext();

        var query = dbContext.ClaudeAssistantEvents
            .AsNoTracking()
            .Where(x => x.Id == eventId && x.OrganizationId == organizationId);

        if (!string.IsNullOrWhiteSpace(userName))
        {
            query = query.Where(x => x.UserName == userName);
        }

        return await query.Select(x => x.Screenshot).FirstOrDefaultAsync();
    }
}
