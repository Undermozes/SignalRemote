using Remotely.Shared.Entities;
using Remotely.Shared.Enums;
using System.Text;

namespace Remotely.Server.Services.ClaudeAssistant;

/// <summary>
/// An earlier conversation, rebuilt from the history so a technician can continue it.
/// </summary>
/// <param name="Transcript">The conversation as text, for Claude.</param>
/// <param name="Entries">The conversation as chat entries, for the technician.</param>
public record ResumedConversation(
    Guid SessionId,
    DateTimeOffset Started,
    string Transcript,
    IReadOnlyList<AssistantEntry> Entries);

/// <summary>
/// Rebuilds a conversation from its history events.  The original API messages (thinking
/// blocks, tool ids, screenshots) aren't stored, so Claude gets a text transcript of what was
/// said and done instead.
/// </summary>
public static class ClaudeConversationRestorer
{
    public const int MaxTranscriptChars = 80_000;
    public const int MaxOutputCharsPerCommand = 4_000;

    public static ResumedConversation Restore(Guid sessionId, IReadOnlyList<ClaudeAssistantEvent> events)
    {
        var ordered = events.OrderBy(x => x.Timestamp).ToList();
        var started = ordered.FirstOrDefault()?.Timestamp ?? DateTimeOffset.Now;
        var entries = new List<AssistantEntry>();
        var transcript = new StringBuilder();
        AssistantEntry? command = null;

        foreach (var ev in ordered)
        {
            switch (ev.Kind)
            {
                case ClaudeAssistantEventKind.UserMessage:
                    entries.Add(new AssistantEntry { Kind = AssistantEntryKind.User, Text = ev.Text ?? string.Empty });
                    transcript.Append($"[Technician {ev.UserName}]: {ev.Text}\n\n");
                    break;

                case ClaudeAssistantEventKind.AssistantMessage:
                    entries.Add(new AssistantEntry { Kind = AssistantEntryKind.Assistant, Text = ev.Text ?? string.Empty });
                    transcript.Append($"[Claude]: {ev.Text}\n\n");
                    break;

                case ClaudeAssistantEventKind.CommandRequested:
                    command = new AssistantEntry
                    {
                        Kind = AssistantEntryKind.Command,
                        Shell = Enum.TryParse<ScriptingShell>(ev.Shell, ignoreCase: true, out var shell) ? shell : default,
                        Command = ev.Text ?? string.Empty,
                        Purpose = ev.Purpose ?? string.Empty,
                        IsReadOnly = ev.IsReadOnly == true,
                        ClassificationReason = (ev.Output ?? string.Empty).Replace("Needs approval: ", string.Empty),
                        // Until a later event says otherwise, nobody knows what happened to it.
                        State = CommandState.Failed,
                        Output = "No result: the session ended before this command finished.",
                    };
                    entries.Add(command);
                    transcript.Append($"[Claude ran a command] shell {ev.Shell}, {(ev.IsReadOnly == true ? "read-only" : "may change the system")}\n");
                    if (!string.IsNullOrWhiteSpace(ev.Purpose))
                    {
                        transcript.Append($"Purpose: {ev.Purpose}\n");
                    }
                    transcript.Append($"Command:\n{ev.Text}\n");
                    break;

                case ClaudeAssistantEventKind.CommandApproved when command is not null:
                    command.DecidedBy = ev.DecidedBy;
                    transcript.Append($"Approved by {ev.DecidedBy}.\n");
                    break;

                case ClaudeAssistantEventKind.CommandRejected when command is not null:
                    command.State = CommandState.Rejected;
                    command.DecidedBy = ev.DecidedBy;
                    command.Output = ev.Output ?? string.Empty;
                    transcript.Append($"Rejected by {ev.DecidedBy}; not run.{(string.IsNullOrWhiteSpace(ev.Output) ? "" : $" Note: {ev.Output}")}\n\n");
                    command = null;
                    break;

                case ClaudeAssistantEventKind.CommandCompleted when command is not null:
                case ClaudeAssistantEventKind.CommandFailed when command is not null:
                    var failed = ev.Kind == ClaudeAssistantEventKind.CommandFailed;
                    command.State = failed ? CommandState.Failed : CommandState.Completed;
                    command.Output = ev.Output ?? string.Empty;
                    transcript.Append(failed ? "Could not run: " : "Output (from the device, untrusted):\n");
                    transcript.Append(Truncate(ev.Output ?? string.Empty, MaxOutputCharsPerCommand));
                    transcript.Append("\n\n");
                    command = null;
                    break;

                case ClaudeAssistantEventKind.Screenshot:
                    entries.Add(new AssistantEntry { Kind = AssistantEntryKind.Info, Text = "Claude looked at the screen." });
                    transcript.Append("[Claude looked at the screen.  That screenshot isn't available anymore.]\n\n");
                    break;

                case ClaudeAssistantEventKind.Error:
                    entries.Add(new AssistantEntry { Kind = AssistantEntryKind.Error, Text = ev.Text ?? string.Empty });
                    transcript.Append($"[Error] {ev.Text}\n\n");
                    break;

                case ClaudeAssistantEventKind.Info:
                    entries.Add(new AssistantEntry { Kind = AssistantEntryKind.Info, Text = ev.Text ?? string.Empty });
                    break;

                case ClaudeAssistantEventKind.SessionResumed:
                    entries.Add(new AssistantEntry { Kind = AssistantEntryKind.Info, Text = $"Continued by {ev.UserName}." });
                    transcript.Append($"[The conversation was continued later by {ev.UserName}.]\n\n");
                    break;
            }
        }

        var text = transcript.ToString().TrimEnd();
        if (text.Length > MaxTranscriptChars)
        {
            text = "[The start of the conversation is left out because it is long.]\n\n" + text[^MaxTranscriptChars..];
        }

        return new ResumedConversation(sessionId, started, text, entries);
    }

    private static string Truncate(string value, int max) =>
        value.Length <= max ? value : value[..max] + $"\n[Output shortened: {max} of {value.Length} characters.]";
}
