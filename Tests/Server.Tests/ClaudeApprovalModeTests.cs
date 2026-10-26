using Anthropic;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using Remotely.Server.Services;
using Remotely.Server.Services.ClaudeAssistant;
using Remotely.Shared.Entities;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Remotely.Server.Tests;

[TestClass]
public class ClaudeApprovalModeTests
{
    private readonly List<ClaudeAssistantEvent> _recorded = [];

    private ClaudeAssistantSession CreateSession(bool isAdmin, bool autoRunReadOnly = true)
    {
        var history = new Mock<IClaudeAssistantHistory>();
        history.Setup(x => x.Record(It.IsAny<ClaudeAssistantEvent>()))
            .Callback<ClaudeAssistantEvent>(_recorded.Add)
            .Returns(Task.CompletedTask);

        return new ClaudeAssistantSession(
            new AnthropicClient { ApiKey = "test" },
            Mock.Of<IAgentCommandRunner>(),
            Mock.Of<IAgentHubSessionCache>(),
            history.Object,
            new ClaudePricing(4, 20, 0.2m, 5),
            NullLogger.Instance,
            new RemotelyUser { UserName = "tech@example.com", IsAdministrator = isAdmin, OrganizationID = "org" },
            new Device { ID = "device", DeviceName = "PC-1", OrganizationID = "org", Platform = "Windows" },
            "claude-opus-5-5",
            autoRunReadOnly);
    }

    [TestMethod]
    public void DefaultMode_FollowsServerConfig()
    {
        Assert.AreEqual(ApprovalMode.AllowReadOnly, CreateSession(isAdmin: true, autoRunReadOnly: true).Mode);
        Assert.AreEqual(ApprovalMode.AskAlways, CreateSession(isAdmin: true, autoRunReadOnly: false).Mode);
    }

    [TestMethod]
    public void AllowAll_OnlyForAdministrators()
    {
        var technician = CreateSession(isAdmin: false);
        Assert.IsFalse(technician.SetMode(ApprovalMode.AllowAll));
        Assert.AreEqual(ApprovalMode.AllowReadOnly, technician.Mode);

        Assert.IsTrue(technician.SetMode(ApprovalMode.AskAlways));
        Assert.AreEqual(ApprovalMode.AskAlways, technician.Mode);

        var admin = CreateSession(isAdmin: true);
        Assert.IsTrue(admin.SetMode(ApprovalMode.AllowAll));
        Assert.AreEqual(ApprovalMode.AllowAll, admin.Mode);
    }

    [TestMethod]
    public void ModeChange_IsShownInChat_ButNoHistoryBeforeFirstMessage()
    {
        var session = CreateSession(isAdmin: true);
        session.SetMode(ApprovalMode.AllowAll);
        session.SetMode(ApprovalMode.AllowAll); // no change, no extra entry

        var changes = session.SnapshotEntries().Where(x => x.Kind == AssistantEntryKind.Info && x.Text.StartsWith("Approval mode")).ToList();
        Assert.AreEqual(1, changes.Count);
        StringAssert.Contains(changes[0].Text, "Allow all");
        StringAssert.Contains(changes[0].Text, "tech@example.com");

        // Nothing asked yet: no history at all, also not when the session ends.
        session.Dispose();
        Assert.AreEqual(0, _recorded.Count);
    }

    [TestMethod]
    public void Modes_HaveNamesAndDescriptions()
    {
        foreach (var mode in System.Enum.GetValues<ApprovalMode>())
        {
            Assert.IsFalse(string.IsNullOrWhiteSpace(mode.DisplayName()));
            Assert.IsFalse(string.IsNullOrWhiteSpace(mode.ShortName()));
            Assert.IsFalse(string.IsNullOrWhiteSpace(mode.Description()));
        }
        Assert.AreEqual("Allow read-only", ApprovalMode.AllowReadOnly.ShortName());
    }

    [TestMethod]
    public void Registry_KeepsSessionsWithAPage_EndsIdleOnes()
    {
        using var registry = new ClaudeAssistantSessionRegistry(NullLogger<ClaudeAssistantSessionRegistry>.Instance);
        var watched = CreateSession(isAdmin: true);
        var idle = CreateSession(isAdmin: true);
        registry.Add(watched);
        registry.Add(idle);
        watched.Attach();

        Assert.IsTrue(registry.TryGetActive("tech@example.com", "device", out _));

        registry.Cleanup(System.DateTimeOffset.Now + ClaudeAssistantSessionRegistry.IdleTimeout + System.TimeSpan.FromMinutes(1));

        Assert.IsTrue(registry.TryGet(watched.SessionId, out _), "A session with an open page keeps running.");
        Assert.IsFalse(registry.TryGet(idle.SessionId, out _), "An idle session without a page ends.");
        Assert.IsTrue(idle.IsEnded);
        Assert.IsFalse(_recorded.Any(x => x.SessionId == idle.SessionId), "A session without questions leaves no history.");

        watched.Detach();
        registry.Cleanup(System.DateTimeOffset.Now + ClaudeAssistantSessionRegistry.IdleTimeout + System.TimeSpan.FromMinutes(1));
        Assert.IsFalse(registry.TryGetActive("tech@example.com", "device", out _));
    }
}
