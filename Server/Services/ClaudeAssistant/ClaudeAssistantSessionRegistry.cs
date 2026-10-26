using System.Collections.Concurrent;

namespace Remotely.Server.Services.ClaudeAssistant;

/// <summary>
/// Keeps Claude sessions alive on the server, independent of the page that shows them.
/// Refreshing the page, a phone screen turning off or a lost connection no longer stops
/// the conversation: the page reattaches to the running session.  Sessions that nobody
/// has looked at for a while are ended.
/// </summary>
public interface IClaudeAssistantSessionRegistry
{
    void Add(ClaudeAssistantSession session);

    bool TryGet(Guid sessionId, out ClaudeAssistantSession session);

    /// <summary>The most recent running session of this technician for this device.</summary>
    bool TryGetActive(string userName, string deviceId, out ClaudeAssistantSession session);

    /// <summary>Ends the session (stops what Claude is doing) and forgets it.</summary>
    void End(ClaudeAssistantSession session);
}

public sealed class ClaudeAssistantSessionRegistry : IClaudeAssistantSessionRegistry, IDisposable
{
    /// <summary>A session without an open page ends after this long without activity.</summary>
    public static readonly TimeSpan IdleTimeout = TimeSpan.FromMinutes(30);

    /// <summary>Upper limit for a session nobody looks at, even while Claude is still working.</summary>
    public static readonly TimeSpan MaxUnattended = TimeSpan.FromHours(4);

    private readonly ConcurrentDictionary<Guid, ClaudeAssistantSession> _sessions = new();
    private readonly ILogger<ClaudeAssistantSessionRegistry> _logger;
    private readonly Timer _cleanupTimer;

    public ClaudeAssistantSessionRegistry(ILogger<ClaudeAssistantSessionRegistry> logger)
    {
        _logger = logger;
        _cleanupTimer = new Timer(_ => Cleanup(DateTimeOffset.Now), null, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));
    }

    public void Add(ClaudeAssistantSession session)
    {
        _sessions[session.SessionId] = session;
    }

    public bool TryGet(Guid sessionId, out ClaudeAssistantSession session)
    {
        if (_sessions.TryGetValue(sessionId, out var found) && !found.IsEnded)
        {
            session = found;
            return true;
        }
        session = null!;
        return false;
    }

    public bool TryGetActive(string userName, string deviceId, out ClaudeAssistantSession session)
    {
        var found = _sessions.Values
            .Where(x => !x.IsEnded && x.Device.ID == deviceId && x.User.UserName == userName)
            .OrderByDescending(x => x.LastActivity)
            .FirstOrDefault();

        session = found!;
        return found is not null;
    }

    public void End(ClaudeAssistantSession session)
    {
        _sessions.TryRemove(session.SessionId, out _);
        session.Dispose();
    }

    /// <summary>Ends sessions without an open page that are idle, or waiting on an approval nobody can give.</summary>
    public void Cleanup(DateTimeOffset now)
    {
        foreach (var session in _sessions.Values)
        {
            if (session.IsEnded)
            {
                _sessions.TryRemove(session.SessionId, out _);
                continue;
            }

            if (session.Viewers > 0)
            {
                continue;
            }

            var idle = now - session.LastActivity;
            var working = session.IsBusy && !session.IsWaitingForApproval;
            if ((idle > IdleTimeout && !working) || idle > MaxUnattended)
            {
                _logger.LogInformation(
                    "[ClaudeAudit] Session {sessionId} ended after {minutes:0} minutes without an open page.",
                    session.SessionId, idle.TotalMinutes);
                End(session);
            }
        }
    }

    public void Dispose()
    {
        _cleanupTimer.Dispose();
        foreach (var session in _sessions.Values)
        {
            session.Dispose();
        }
        _sessions.Clear();
    }
}
