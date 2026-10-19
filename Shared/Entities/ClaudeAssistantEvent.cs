using System.ComponentModel.DataAnnotations;

namespace Remotely.Shared.Entities;

public enum ClaudeAssistantEventKind
{
    SessionStarted,
    UserMessage,
    AssistantMessage,
    CommandRequested,
    CommandApproved,
    CommandRejected,
    CommandCompleted,
    CommandFailed,
    Screenshot,
    Info,
    Error,
    SessionEnded,
    /// <summary>Token usage of one Claude API call; JSON in <see cref="ClaudeAssistantEvent.Output"/>.</summary>
    Usage,
}

/// <summary>
/// One step in a Claude assistant session, kept as an audit history of everything Claude
/// saw and did.  Deliberately not linked to devices or users with foreign keys, so the
/// history survives when a device or user is deleted.  Old events are removed with the
/// server's data retention setting.
/// </summary>
public class ClaudeAssistantEvent
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid SessionId { get; set; }

    /// <summary>Always UTC, so ordering and date filters work whatever the server's time zone.</summary>
    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;

    public ClaudeAssistantEventKind Kind { get; set; }

    [StringLength(128)]
    public string OrganizationId { get; set; } = string.Empty;

    [StringLength(128)]
    public string DeviceId { get; set; } = string.Empty;

    public string? DeviceName { get; set; }

    /// <summary>The technician who ran the session.</summary>
    public string? UserName { get; set; }

    /// <summary>Message text, the command, or the error.</summary>
    public string? Text { get; set; }

    public string? Shell { get; set; }

    /// <summary>Why Claude wanted to run the command, as shown to the technician.</summary>
    public string? Purpose { get; set; }

    public bool? IsReadOnly { get; set; }

    /// <summary>Command output, a rejection note, or why approval was needed.</summary>
    public string? Output { get; set; }

    /// <summary>Who approved or rejected a command ("auto (read-only)" for auto-run).</summary>
    public string? DecidedBy { get; set; }

    /// <summary>JPEG of the screen Claude looked at.</summary>
    public byte[]? Screenshot { get; set; }
}
