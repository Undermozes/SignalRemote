using Microsoft.VisualStudio.TestTools.UnitTesting;
using Remotely.Server.Services.ClaudeAssistant;
using Remotely.Shared.Entities;
using Remotely.Shared.Enums;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Remotely.Server.Tests;

[TestClass]
public class ClaudeConversationRestorerTests
{
    private readonly Guid _sessionId = Guid.NewGuid();
    private readonly DateTimeOffset _start = new(2026, 10, 19, 8, 0, 0, TimeSpan.Zero);
    private int _step;

    private ClaudeAssistantEvent Event(ClaudeAssistantEventKind kind, string? text = null, Action<ClaudeAssistantEvent>? fill = null)
    {
        var ev = new ClaudeAssistantEvent
        {
            SessionId = _sessionId,
            Timestamp = _start.AddSeconds(_step++),
            Kind = kind,
            OrganizationId = "org",
            DeviceId = "device",
            DeviceName = "PC-1",
            UserName = "tech@example.com",
            Text = text,
        };
        fill?.Invoke(ev);
        return ev;
    }

    [TestMethod]
    public void Restore_RebuildsChatAndTranscript()
    {
        var events = new List<ClaudeAssistantEvent>
        {
            Event(ClaudeAssistantEventKind.SessionStarted, "Model: claude-opus-5-5"),
            Event(ClaudeAssistantEventKind.UserMessage, "Why is Chrome slow?"),
            Event(ClaudeAssistantEventKind.Usage, null, x => x.Output = "{}"),
            Event(ClaudeAssistantEventKind.CommandRequested, "Get-Process chrome", x => { x.Shell = "PSCore"; x.Purpose = "Check CPU."; x.IsReadOnly = true; }),
            Event(ClaudeAssistantEventKind.CommandApproved, null, x => x.DecidedBy = "auto (read-only)"),
            Event(ClaudeAssistantEventKind.CommandCompleted, "Get-Process chrome", x => x.Output = "chrome 812 CPU"),
            Event(ClaudeAssistantEventKind.Screenshot, "1280x800"),
            Event(ClaudeAssistantEventKind.CommandRequested, "Remove-Item C:\\cache -Recurse", x => { x.Shell = "PSCore"; x.IsReadOnly = false; x.Output = "Needs approval: Remove-Item"; }),
            Event(ClaudeAssistantEventKind.CommandRejected, null, x => { x.DecidedBy = "tech@example.com"; x.Output = "Close Chrome first."; }),
            Event(ClaudeAssistantEventKind.AssistantMessage, "One tab uses 1.9 GB."),
            Event(ClaudeAssistantEventKind.SessionEnded),
        };

        var resumed = ClaudeConversationRestorer.Restore(_sessionId, events);

        Assert.AreEqual(_sessionId, resumed.SessionId);
        Assert.AreEqual(_start, resumed.Started);
        CollectionAssert.AreEqual(
            new[] { AssistantEntryKind.User, AssistantEntryKind.Command, AssistantEntryKind.Info, AssistantEntryKind.Command, AssistantEntryKind.Assistant },
            resumed.Entries.Select(x => x.Kind).ToArray());

        var completed = resumed.Entries[1];
        Assert.AreEqual(CommandState.Completed, completed.State);
        Assert.AreEqual(ScriptingShell.PSCore, completed.Shell);
        Assert.AreEqual("chrome 812 CPU", completed.Output);
        Assert.AreEqual("auto (read-only)", completed.DecidedBy);

        var rejected = resumed.Entries[3];
        Assert.AreEqual(CommandState.Rejected, rejected.State);
        Assert.AreEqual("Close Chrome first.", rejected.Output);
        Assert.AreEqual("Remove-Item", rejected.ClassificationReason);

        StringAssert.Contains(resumed.Transcript, "[Technician tech@example.com]: Why is Chrome slow?");
        StringAssert.Contains(resumed.Transcript, "chrome 812 CPU");
        StringAssert.Contains(resumed.Transcript, "Rejected by tech@example.com; not run. Note: Close Chrome first.");
        StringAssert.Contains(resumed.Transcript, "That screenshot isn't available anymore.");
        StringAssert.Contains(resumed.Transcript, "[Claude]: One tab uses 1.9 GB.");
    }

    [TestMethod]
    public void Restore_CommandWithoutResult_IsMarkedUnknown()
    {
        var events = new List<ClaudeAssistantEvent>
        {
            Event(ClaudeAssistantEventKind.UserMessage, "Restart the spooler."),
            Event(ClaudeAssistantEventKind.CommandRequested, "Restart-Service Spooler", x => x.Shell = "PSCore"),
            Event(ClaudeAssistantEventKind.SessionEnded),
        };

        var command = ClaudeConversationRestorer.Restore(_sessionId, events).Entries[1];

        Assert.AreEqual(CommandState.Failed, command.State);
        StringAssert.Contains(command.Output, "ended before this command finished");
    }

    [TestMethod]
    public void Restore_LongConversation_KeepsTheEnd()
    {
        var events = new List<ClaudeAssistantEvent>();
        for (var i = 0; i < 100; i++)
        {
            events.Add(Event(ClaudeAssistantEventKind.UserMessage, $"Question {i} " + new string('x', 2_000)));
        }

        var transcript = ClaudeConversationRestorer.Restore(_sessionId, events).Transcript;

        Assert.IsTrue(transcript.Length <= ClaudeConversationRestorer.MaxTranscriptChars + 100);
        StringAssert.StartsWith(transcript, "[The start of the conversation is left out");
        StringAssert.Contains(transcript, "Question 99 ");
        Assert.IsFalse(transcript.Contains("Question 0 "));
    }

    [TestMethod]
    public void Restore_LongOutput_IsShortened()
    {
        var events = new List<ClaudeAssistantEvent>
        {
            Event(ClaudeAssistantEventKind.CommandRequested, "Get-WinEvent", x => x.Shell = "PSCore"),
            Event(ClaudeAssistantEventKind.CommandCompleted, "Get-WinEvent", x => x.Output = new string('y', 10_000)),
        };

        var resumed = ClaudeConversationRestorer.Restore(_sessionId, events);

        StringAssert.Contains(resumed.Transcript, $"[Output shortened: {ClaudeConversationRestorer.MaxOutputCharsPerCommand} of 10000 characters.]");
        // The technician still sees the full output.
        Assert.AreEqual(10_000, resumed.Entries[0].Output.Length);
    }
}
