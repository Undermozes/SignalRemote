using Microsoft.EntityFrameworkCore;
using Remotely.Server.Data;
using Remotely.Shared.Entities;

namespace Remotely.Server.Services.ClaudeAssistant;

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
    int Errors);

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

        var sessionIds = pageStarts.Select(x => x.SessionId).ToList();

        // Summarize in memory: one page of sessions is small, and it keeps the query
        // simple for every database provider.
        var events = await dbContext.ClaudeAssistantEvents
            .AsNoTracking()
            .Where(x => sessionIds.Contains(x.SessionId))
            .Select(x => new { x.SessionId, x.Kind, x.Timestamp, x.Text })
            .ToListAsync();

        var sessions = pageStarts
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
                    sessionEvents.Count(x => x.Kind is ClaudeAssistantEventKind.Error or ClaudeAssistantEventKind.CommandFailed));
            })
            .ToList();

        return new ClaudeHistoryPage(sessions, total);
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
