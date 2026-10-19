using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Remotely.Server.Data;
using Remotely.Server.Services.ClaudeAssistant;
using Remotely.Shared.Entities;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace Remotely.Server.Tests;

[TestClass]
public class ClaudeAssistantHistoryTests
{
    private ClaudeAssistantHistory _history = null!;
    private string _orgId = null!;

    [TestInitialize]
    public void Init()
    {
        _history = new ClaudeAssistantHistory(
            IoCActivator.ServiceProvider.GetRequiredService<IAppDbFactory>(),
            NullLogger<ClaudeAssistantHistory>.Instance);
        // A fresh organization per test keeps tests independent.
        _orgId = Guid.NewGuid().ToString();
    }

    private async Task<Guid> AddSession(string userName, string deviceName, string question, DateTimeOffset start)
    {
        var sessionId = Guid.NewGuid();
        var time = start;
        async Task Add(ClaudeAssistantEventKind kind, Action<ClaudeAssistantEvent>? fill = null)
        {
            time = time.AddSeconds(1);
            var ev = new ClaudeAssistantEvent
            {
                SessionId = sessionId,
                Kind = kind,
                Timestamp = time,
                OrganizationId = _orgId,
                DeviceId = $"device-{deviceName}",
                DeviceName = deviceName,
                UserName = userName,
            };
            fill?.Invoke(ev);
            await _history.Record(ev);
        }

        await Add(ClaudeAssistantEventKind.SessionStarted);
        await Add(ClaudeAssistantEventKind.UserMessage, x => x.Text = question);
        await Add(ClaudeAssistantEventKind.CommandRequested, x => x.Text = "Get-Process");
        await Add(ClaudeAssistantEventKind.CommandApproved, x => x.DecidedBy = "auto (read-only)");
        await Add(ClaudeAssistantEventKind.CommandCompleted, x => x.Output = "chrome 95%");
        await Add(ClaudeAssistantEventKind.Screenshot, x => x.Screenshot = [1, 2, 3]);
        await Add(ClaudeAssistantEventKind.Usage, x => x.Output = System.Text.Json.JsonSerializer.Serialize(
            new ClaudeUsage(1000, 200, 5000, 300, 0.0123m)));
        await Add(ClaudeAssistantEventKind.AssistantMessage, x => x.Text = "Chrome uses most of the CPU.");
        await Add(ClaudeAssistantEventKind.Usage, x => x.Output = System.Text.Json.JsonSerializer.Serialize(
            new ClaudeUsage(500, 100, 6000, 0, 0.0100m)));
        await Add(ClaudeAssistantEventKind.SessionEnded);
        return sessionId;
    }

    [TestMethod]
    public async Task GetSessions_SummarizesNewestFirst()
    {
        var start = DateTimeOffset.Now.AddHours(-2);
        var older = await AddSession("alice", "PC-1", "Why is Resengo slow?", start);
        var newer = await AddSession("bob", "PC-2", "Printer offline", start.AddHours(1));

        var page = await _history.GetSessions(_orgId, null, null, 0, 20);

        Assert.AreEqual(2, page.TotalSessions);
        Assert.AreEqual(newer, page.Sessions[0].SessionId);
        Assert.AreEqual(older, page.Sessions[1].SessionId);

        var summary = page.Sessions[1];
        Assert.AreEqual("alice", summary.UserName);
        Assert.AreEqual("PC-1", summary.DeviceName);
        Assert.AreEqual("Why is Resengo slow?", summary.FirstQuestion);
        Assert.AreEqual(1, summary.Commands);
        Assert.AreEqual(1, summary.Screenshots);
        Assert.AreEqual(0, summary.Errors);
    }

    [TestMethod]
    public async Task UserFilter_HidesOtherTechniciansSessions()
    {
        var start = DateTimeOffset.Now.AddHours(-1);
        var aliceSession = await AddSession("alice", "PC-1", "Question A", start);
        var bobSession = await AddSession("bob", "PC-2", "Question B", start);

        var page = await _history.GetSessions(_orgId, "alice", null, 0, 20);
        Assert.AreEqual(1, page.TotalSessions);
        Assert.AreEqual(aliceSession, page.Sessions[0].SessionId);

        Assert.AreEqual(0, (await _history.GetSessionEvents(_orgId, "alice", bobSession)).Count);

        var bobScreenshot = (await _history.GetSessionEvents(_orgId, null, bobSession))
            .Single(x => x.Kind == ClaudeAssistantEventKind.Screenshot);
        Assert.IsNull(await _history.GetScreenshot(_orgId, "alice", bobScreenshot.Id));
        CollectionAssert.AreEqual(new byte[] { 1, 2, 3 }, await _history.GetScreenshot(_orgId, null, bobScreenshot.Id));
    }

    [TestMethod]
    public async Task OtherOrganizations_AreNotVisible()
    {
        var session = await AddSession("alice", "PC-1", "Question", DateTimeOffset.Now.AddMinutes(-5));
        var otherOrg = Guid.NewGuid().ToString();

        Assert.AreEqual(0, (await _history.GetSessions(otherOrg, null, null, 0, 20)).TotalSessions);
        Assert.AreEqual(0, (await _history.GetSessionEvents(otherOrg, null, session)).Count);
    }

    [TestMethod]
    public async Task GetSessionEvents_ReturnsTimelineWithoutScreenshotBytes()
    {
        var session = await AddSession("alice", "PC-1", "Question", DateTimeOffset.Now.AddMinutes(-5));

        var events = await _history.GetSessionEvents(_orgId, null, session);

        Assert.AreEqual(10, events.Count);
        Assert.AreEqual(ClaudeAssistantEventKind.SessionStarted, events[0].Kind);
        Assert.AreEqual(ClaudeAssistantEventKind.SessionEnded, events[^1].Kind);
        Assert.IsTrue(events.All(x => x.Screenshot is null));
    }

    [TestMethod]
    public async Task Summary_SumsTokensAndCost()
    {
        await AddSession("alice", "PC-1", "Question", DateTimeOffset.UtcNow.AddMinutes(-5));

        var usage = (await _history.GetSessions(_orgId, null, null, 0, 20)).Sessions.Single().Usage;

        Assert.AreEqual(1500, usage.Input);
        Assert.AreEqual(300, usage.Output);
        Assert.AreEqual(11000, usage.CacheRead);
        Assert.AreEqual(300, usage.CacheWrite);
        Assert.AreEqual(0.0223m, usage.CostUsd);
    }

    [TestMethod]
    public async Task GetSessionsInRange_OnlyReturnsSessionsStartedInRange()
    {
        var now = DateTimeOffset.UtcNow;
        var inRange = await AddSession("alice", "PC-1", "In range", now.AddDays(-2));
        await AddSession("alice", "PC-2", "Too old", now.AddDays(-40));

        var sessions = await _history.GetSessionsInRange(_orgId, null, now.AddDays(-7), now);

        Assert.AreEqual(1, sessions.Count);
        Assert.AreEqual(inRange, sessions[0].SessionId);
    }

    [TestMethod]
    public void Pricing_ComputesCostPerMillionTokens()
    {
        // claude-opus-5-5 list prices: $4 input, $20 output, $0.20 cache read, $5 cache write.
        var pricing = new ClaudePricing(4m, 20m, 0.20m, 5m);

        var cost = pricing.CostOf(input: 1_000_000, output: 100_000, cacheRead: 2_000_000, cacheWrite: 200_000);

        Assert.AreEqual(4m + 2m + 0.4m + 1m, cost);
    }

    [TestMethod]
    public void Usage_ParsesBadJsonAsZero()
    {
        Assert.AreEqual(0, ClaudeUsage.Parse("not json").Total);
        Assert.AreEqual(0, ClaudeUsage.Parse(null).Total);
    }

    [TestMethod]
    public async Task Search_MatchesQuestionsAndCommands()
    {
        var start = DateTimeOffset.Now.AddHours(-1);
        var resengo = await AddSession("alice", "PC-1", "Why is Resengo slow?", start);
        await AddSession("alice", "PC-2", "Printer offline", start);

        var page = await _history.GetSessions(_orgId, null, "Resengo", 0, 20);

        Assert.AreEqual(1, page.TotalSessions);
        Assert.AreEqual(resengo, page.Sessions[0].SessionId);
    }
}
