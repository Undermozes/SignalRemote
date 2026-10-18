using Remotely.Shared.Enums;

namespace Remotely.Server.Models;

public class SettingsModel
{
    public static Guid DbKey { get; } = Guid.Parse("a35d6212-c0b7-49b2-89e1-7ba497f94a35");

    /// <summary>
    /// Allows agents that predate device secrets to connect.  Leave enabled until all agents
    /// have updated, then disable it so every device must authenticate with its secret.
    /// </summary>
    public bool AllowAgentsWithoutDeviceSecret { get; set; } = true;
    public bool AllowApiLogin { get; set; }

    /// <summary>
    /// Enables the Claude AI assistant that can investigate devices via the agent.
    /// </summary>
    public bool ClaudeAssistantEnabled { get; set; }

    /// <summary>
    /// Anthropic API key.  If empty, the ANTHROPIC_API_KEY environment variable is used.
    /// </summary>
    public string ClaudeApiKey { get; set; } = string.Empty;

    public string ClaudeModel { get; set; } = "claude-opus-5-5";

    /// <summary>
    /// When true, commands that were verified as read-only run without approval.
    /// Commands that may change the system always require approval.
    /// </summary>
    public bool ClaudeAutoRunReadOnlyCommands { get; set; } = true;
    public List<string> BannedDevices { get; set; } = [];
    public double DataRetentionInDays { get; set; } = 90;
    public string DbProvider { get; set; } = "SQLite";
    public bool EnableRemoteControlRecording { get; set; }
    public bool EnableWindowsEventLog { get; set; }
    public bool EnforceAttendedAccess { get; set; }
    public bool ForceClientHttps { get; set; }
    public List<string> KnownProxies { get; set; } = [];
    public int MaxConcurrentUpdates { get; set; } = 10;
    public int MaxOrganizationCount { get; set; } = 1;
    public string MessageOfTheDay { get; set; } = string.Empty;
    public bool RedirectToHttps { get; set; } = true;
    public bool RemoteControlNotifyUser { get; set; } = true;
    public bool RemoteControlRequiresAuthentication { get; set; } = true;
    public bool Require2FA { get; set; }
    public string ServerUrl { get; set; } = string.Empty;
    public bool SmtpCheckCertificateRevocation { get; set; } = true;
    public string SmtpDisplayName { get; set; } = string.Empty;
    public string SmtpEmail { get; set; } = string.Empty;
    public string SmtpHost { get; set; } = string.Empty;
    public string SmtpLocalDomain { get; set; } = string.Empty;
    public string SmtpPassword { get; set; } = string.Empty;
    public int SmtpPort { get; set; } = 587;
    public string SmtpUserName { get; set; } = string.Empty;
    public Theme Theme { get; set; } = Theme.Dark;
    public List<string> TrustedCorsOrigins { get; set; } = [];
    public bool UseHsts { get; set; }
    public bool UseHttpLogging { get; set; }
}
