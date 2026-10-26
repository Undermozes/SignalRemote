using Microsoft.VisualStudio.TestTools.UnitTesting;
using Remotely.Server.Services.ClaudeAssistant;
using Remotely.Shared.Entities;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Remotely.Server.Tests;

[TestClass]
public class ClaudeConversationPdfTests
{
    private readonly Guid _sessionId = Guid.NewGuid();
    private readonly DateTimeOffset _start = new(2026, 10, 19, 13, 0, 0, TimeSpan.Zero);
    private int _step;

    private ClaudeAssistantEvent Event(ClaudeAssistantEventKind kind, string? text = null, Action<ClaudeAssistantEvent>? fill = null)
    {
        var ev = new ClaudeAssistantEvent
        {
            SessionId = _sessionId,
            Timestamp = _start.AddSeconds(_step++ * 7),
            Kind = kind,
            OrganizationId = "org",
            DeviceId = "device",
            DeviceName = "GEBRUIKER",
            UserName = "tech@example.com",
            Text = text,
        };
        fill?.Invoke(ev);
        return ev;
    }

    private static byte[] Jpeg()
    {
        using var bitmap = new SKBitmap(320, 200);
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(SKColors.SteelBlue);
        }
        using var data = bitmap.Encode(SKEncodedImageFormat.Jpeg, 80);
        return data.ToArray();
    }

    [TestMethod]
    public void Render_FullConversation_ProducesMultiPagePdf()
    {
        var screenshot = Event(ClaudeAssistantEventKind.Screenshot, "320x200");
        var longOutput = string.Join('\n', Enumerable.Range(1, 400).Select(i => $"chrome {i,5} {new string('x', 140)}"));
        var events = new List<ClaudeAssistantEvent>
        {
            Event(ClaudeAssistantEventKind.SessionStarted, "Model: claude-opus-5-5"),
            Event(ClaudeAssistantEventKind.UserMessage, "Onderzoek waarom Resengo zo traag is in Chrome. 🚀 Één tab hangt."),
            // Blank lines and trailing newlines (crashed the export in production).
            Event(ClaudeAssistantEventKind.UserMessage, "Eerste alinea.\n\nTweede alinea.\n\n\n"),
            Event(ClaudeAssistantEventKind.UserMessage, "\n"),
            Event(ClaudeAssistantEventKind.CommandRequested, "Get-Date", x => { x.Shell = "PSCore"; x.Purpose = "Regel 1\n\nRegel 2"; x.IsReadOnly = true; }),
            screenshot,
            Event(ClaudeAssistantEventKind.CommandRequested, "Get-Process chrome | Sort-Object CPU -Descending", x => { x.Shell = "PSCore"; x.Purpose = "Check CPU."; x.IsReadOnly = true; }),
            Event(ClaudeAssistantEventKind.CommandApproved, null, x => x.DecidedBy = "auto (read-only)"),
            Event(ClaudeAssistantEventKind.CommandCompleted, null, x => x.Output = longOutput),
            Event(ClaudeAssistantEventKind.CommandRequested, "Remove-Item C:\\cache -Recurse", x => { x.Shell = "PSCore"; x.IsReadOnly = false; }),
            Event(ClaudeAssistantEventKind.CommandRejected, null, x => { x.DecidedBy = "tech@example.com"; x.Output = "Close Chrome first."; }),
            Event(ClaudeAssistantEventKind.AssistantMessage,
                "# Conclusion\n\nOne **Chrome tab** uses `1.9 GB`.\n\n- Restart Chrome\n  - check extensions\n1. Update\n\n" +
                "| A | B |\n|---|---|\n| 1 | 2 |\n\n```powershell\nGet-Process\n```\n> Note\n" + new string('w', 300)),
            Event(ClaudeAssistantEventKind.Error, "Claude API error: overloaded"),
            Event(ClaudeAssistantEventKind.SessionResumed, "Model: claude-opus-5-5"),
            Event(ClaudeAssistantEventKind.SessionEnded),
        };

        var pdf = ClaudeConversationPdf.Render(events, new Dictionary<Guid, byte[]> { [screenshot.Id] = Jpeg() }, TimeZoneInfo.Utc);

        var text = Encoding.Latin1.GetString(pdf);
        StringAssert.StartsWith(text, "%PDF-");
        Assert.IsTrue(text.TrimEnd().EndsWith("%%EOF"), "PDF is complete.");
        Assert.IsTrue(CountPages(text) >= 3, $"Expected several pages, got {CountPages(text)}.");
        Assert.IsTrue(text.Contains("/Subtype /Image"), "The screenshot is embedded.");
        File.WriteAllBytes(Path.Combine(Path.GetTempPath(), "claude-conversation-test.pdf"), pdf);
    }

    [TestMethod]
    public void Render_MissingScreenshotAndEmptyTexts_DoesNotThrow()
    {
        var events = new List<ClaudeAssistantEvent>
        {
            Event(ClaudeAssistantEventKind.UserMessage, ""),
            Event(ClaudeAssistantEventKind.Screenshot, "1280x800"),
            Event(ClaudeAssistantEventKind.AssistantMessage, null),
            Event(ClaudeAssistantEventKind.CommandCompleted, null),
        };

        var pdf = ClaudeConversationPdf.Render(events, new Dictionary<Guid, byte[]>(), TimeZoneInfo.Utc);

        StringAssert.StartsWith(Encoding.Latin1.GetString(pdf), "%PDF-");
    }

    private static int CountPages(string pdf) =>
        System.Text.RegularExpressions.Regex.Matches(pdf, @"/Type\s*/Page[^s]").Count;
}
