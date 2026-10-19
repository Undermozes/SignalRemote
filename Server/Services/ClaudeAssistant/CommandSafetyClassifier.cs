using Remotely.Shared.Enums;
using System.Text.RegularExpressions;

namespace Remotely.Server.Services.ClaudeAssistant;

public enum CommandRisk
{
    /// <summary>Only reads system state.  May run without approval.</summary>
    ReadOnly,

    /// <summary>May change the system, or couldn't be proven read-only.  Needs approval.</summary>
    RequiresApproval,
}

public record CommandClassification(CommandRisk Risk, string Reason);

/// <summary>
/// Conservative, allowlist-based classifier for commands the AI assistant wants to run.
/// A command is only treated as read-only when every statement in it starts with a
/// known read-only command and it contains no constructs that could hide other
/// commands (redirection, sub-expressions, script blocks, encoded commands, etc.).
/// Anything else requires a technician's approval.  This is a safety net, not a sandbox:
/// the approval step and the audit log are the real controls.
/// </summary>
public static partial class CommandSafetyClassifier
{
    private const int MaxReadOnlyLength = 2_000;

    // "format-", "find-" and "search-" are not here: Format-Volume, Find-Package and
    // Search-Mailbox -DeleteContent change things.  The formatting cmdlets are listed below.
    private static readonly string[] _psReadOnlyVerbs =
    [
        "get-", "test-", "measure-", "select-", "where-", "sort-",
        "group-", "compare-", "resolve-",
    ];

    private static readonly HashSet<string> _psReadOnlyCommands = new(StringComparer.OrdinalIgnoreCase)
    {
        // ForEach-Object / % are not here: "Get-Process x | % Kill" calls a method without
        // a script block.  Network tools (ping, tracert, nslookup, ...) aren't either: their
        // target host is chosen by the model, so they could carry data off the device.
        "out-string", "convertto-json", "convertto-csv", "convertto-html", "write-output",
        "format-table", "format-list", "format-wide", "format-custom",
        "?", "select", "where", "sort", "ft", "fl", "gci", "ls", "dir",
        "gps", "gsv", "gwmi", "cat", "type", "gc", "hostname", "whoami", "ipconfig", "systeminfo",
        "netstat", "tasklist", "driverquery", "getmac", "route",
        "arp", "quser", "query", "ver", "wevtutil", "gpresult", "w32tm", "powercfg", "manage-bde",
        "netsh", "sc", "reg", "dism", "chkdsk", "fsutil", "wmic", "net",
    };

    // Subcommands that make an otherwise read-only Windows tool safe.  Without one of
    // these, the tool needs approval.
    private static readonly Dictionary<string, Regex> _restrictedTools = new(StringComparer.OrdinalIgnoreCase)
    {
        ["sc"] = ReadOnlyScRegex(),
        ["reg"] = ReadOnlyRegRegex(),
        ["netsh"] = ReadOnlyNetshRegex(),
        ["wevtutil"] = ReadOnlyWevtutilRegex(),
        ["dism"] = ReadOnlyDismRegex(),
        ["chkdsk"] = ReadOnlyChkdskRegex(),
        ["fsutil"] = ReadOnlyFsutilRegex(),
        ["w32tm"] = ReadOnlyW32tmRegex(),
        ["powercfg"] = ReadOnlyPowercfgRegex(),
        ["manage-bde"] = ReadOnlyManageBdeRegex(),
        ["wmic"] = ReadOnlyWmicRegex(),
        ["net"] = ReadOnlyNetRegex(),
        ["route"] = ReadOnlyRouteRegex(),
        ["query"] = ReadOnlyQueryRegex(),
        ["ipconfig"] = new(@"^ipconfig(\.exe)?(\s+/(all|displaydns|allcompartments))*\s*$", RegexOptions.IgnoreCase),
        ["arp"] = new(@"^arp(\.exe)?\s+-a\b", RegexOptions.IgnoreCase),
        ["gpresult"] = new(@"^gpresult(\.exe)?(\s+/(r|z|v|scope\s+(user|computer)))*\s*$", RegexOptions.IgnoreCase),
    };

    private static readonly HashSet<string> _bashReadOnlyCommands = new(StringComparer.Ordinal)
    {
        "cat", "ls", "df", "du", "free", "uptime", "uname", "hostname", "whoami", "id", "ps",
        // No network tools (ping, dig, ...) and no env/printenv (environment secrets).
        "top", "lsblk", "lscpu", "lsusb", "lspci", "lsof", "ip", "ss", "netstat",
        "journalctl", "dmesg", "head", "tail",
        "grep", "egrep", "wc", "sort", "uniq", "cut", "awk", "date", "last", "w", "who",
        "stat", "file", "findmnt", "mount", "lsmod", "sw_vers",
        "system_profiler", "ifconfig", "scutil", "pmset", "diskutil", "log", "launchctl",
        "systemctl", "service", "dpkg", "rpm", "apt", "timedatectl", "hostnamectl", "echo",
        "which", "type", "sensors", "smartctl", "vmstat", "iostat", "nproc", "getent",
    };

    private static readonly Dictionary<string, Regex> _bashRestrictedTools = new(StringComparer.Ordinal)
    {
        ["systemctl"] = new(@"^systemctl\s+(status|is-active|is-enabled|is-failed|list-units|list-unit-files|list-timers|show|cat)\b", RegexOptions.IgnoreCase),
        ["service"] = new(@"^service\s+\S+\s+status\s*$|^service\s+--status-all\s*$"),
        ["apt"] = new(@"^apt\s+(list|show|policy)\b"),
        ["dpkg"] = new(@"^dpkg\s+(-l|-s|-L|--list|--status|--listfiles)\b"),
        ["rpm"] = new(@"^rpm\s+-q(?!.*--(pipe|scripts|triggers|dbpath|root))"),
        ["diskutil"] = new(@"^diskutil\s+(list|info|apfs\s+list)\b"),
        ["log"] = new(@"^log\s+(show|stats)\b"),
        ["launchctl"] = new(@"^launchctl\s+(list|print)\b"),
        ["mount"] = new(@"^mount\s*$"),
        ["timedatectl"] = new(@"^timedatectl\s*(status)?\s*$"),
        ["hostnamectl"] = new(@"^hostnamectl\s*(status)?\s*$"),
        ["pmset"] = new(@"^pmset\s+-g\b"),
        ["scutil"] = new(@"^scutil\s+(--dns|--proxy|--nwi|--get)\b"),
        ["smartctl"] = new(@"^smartctl\s+(-a|-i|-H|--all|--info|--health)\b(?!.*(\s-[tsoSX]\b|--(test|smart|offlineauto|saveauto|abort|set)))"),
        ["top"] = new(@"^top\s+.*-b"),
        ["ip"] = new(@"^ip\s+(-\S+\s+)*(a|addr|address|r|route|l|link|n|neigh|neighbour)(\s+(show|list)(\s+[\w.-]+)*)?\s*$"),
        ["date"] = new(@"^date(\s+(-u|-R|-I\w*|--utc|--iso-8601(=\w+)?|\+\S+))*\s*$"),
        ["hostname"] = new(@"^hostname(\s+(-f|-I|-i|-s|-d|--fqdn|--all-ip-addresses))*\s*$"),
        ["ifconfig"] = new(@"^ifconfig(\s+(-a|[\w.:-]+))?\s*$"),
        ["dmesg"] = new(@"^dmesg(\s+(-T|-H|-x|-k|-u|-l\s+\S+|--level=\S+|--ctime|--human|-L|--color=\w+|--time-format=\S+))*\s*$"),
        ["journalctl"] = new(@"^journalctl(?!.*--(vacuum|rotate|flush|sync|relinquish|setup-keys|update-catalog|cursor-file))"),
        ["uniq"] = new(@"^uniq(\s+-\S+)*\s*$"),
        ["sort"] = new(@"^sort(?!.*(\s-o|--output|--compress-program|\s-T|--temporary-directory|--files0-from))"),
        ["ss"] = new(@"^ss(?!.*(\s-K|--kill))"),
        ["sensors"] = new(@"^sensors(?!.*(\s-s|--set))"),
        ["file"] = new(@"^file(?!.*(\s-C|--compile))"),
    };

    public static CommandClassification Classify(ScriptingShell shell, string command)
    {
        if (string.IsNullOrWhiteSpace(command))
        {
            return new(CommandRisk.RequiresApproval, "Empty command.");
        }

        if (command.Length > MaxReadOnlyLength)
        {
            return new(CommandRisk.RequiresApproval, "Command is too long to verify automatically.");
        }

        // UNC paths reach other machines (and send the device's credentials to them).
        if (command.Contains(@"\\", StringComparison.Ordinal) || command.Contains("//", StringComparison.Ordinal))
        {
            return new(CommandRisk.RequiresApproval, "Refers to a network path.");
        }

        if (SensitiveDataRegex().IsMatch(command))
        {
            return new(CommandRisk.RequiresApproval, "Reads credentials or other secrets.");
        }

        return shell switch
        {
            ScriptingShell.PSCore or ScriptingShell.WinPS => ClassifyPowerShell(command),
            ScriptingShell.CMD => ClassifyCmd(command),
            ScriptingShell.Bash => ClassifyBash(command),
            _ => new(CommandRisk.RequiresApproval, "Unknown shell."),
        };
    }

    private static CommandClassification ClassifyPowerShell(string command)
    {
        // Constructs that can run arbitrary code or write files.
        if (PsDangerousRegex().IsMatch(command))
        {
            return new(CommandRisk.RequiresApproval, "Contains redirection, sub-expressions, script blocks or dynamic invocation.");
        }

        if (PsRemoteTargetRegex().IsMatch(command))
        {
            return new(CommandRisk.RequiresApproval, "Targets another computer.");
        }

        foreach (var segment in SplitStatements(command, ['|', ';', '\n', '\r']))
        {
            var name = FirstToken(segment);
            if (name is null)
            {
                continue;
            }

            if (_psReadOnlyVerbs.Any(v => name.StartsWith(v, StringComparison.OrdinalIgnoreCase)))
            {
                if (PsNeverReadOnlyRegex().IsMatch(name))
                {
                    return new(CommandRisk.RequiresApproval, $"'{name}' is not treated as read-only.");
                }
                continue;
            }

            // In Windows PowerShell 5.1, "sc" is an alias of Set-Content; only sc.exe is the
            // service tool.
            if (name.Equals("sc", StringComparison.OrdinalIgnoreCase))
            {
                return new(CommandRisk.RequiresApproval, "In PowerShell 'sc' can mean Set-Content; use sc.exe.");
            }

            var exe = StripExe(name);
            if (!_psReadOnlyCommands.Contains(exe))
            {
                return new(CommandRisk.RequiresApproval, $"'{name}' is not on the read-only list.");
            }

            if (_restrictedTools.TryGetValue(exe, out var allowed) && !allowed.IsMatch(segment.Trim()))
            {
                return new(CommandRisk.RequiresApproval, $"'{name}' is only read-only with specific arguments.");
            }
        }

        return new(CommandRisk.ReadOnly, "All statements are read-only commands.");
    }

    private static CommandClassification ClassifyCmd(string command)
    {
        if (CmdDangerousRegex().IsMatch(command))
        {
            return new(CommandRisk.RequiresApproval, "Contains redirection, variables or command chaining.");
        }

        foreach (var segment in SplitStatements(command, ['|', '\n', '\r']))
        {
            var name = FirstToken(segment);
            if (name is null)
            {
                continue;
            }

            var exe = StripExe(name);
            if (exe is "findstr" or "find" or "more")
            {
                continue;
            }

            // sort /o writes a file, /t picks a temp directory.
            if (exe is "sort")
            {
                if (CmdSortWriteRegex().IsMatch(segment))
                {
                    return new(CommandRisk.RequiresApproval, "'sort' with /o or /t writes files.");
                }
                continue;
            }

            if (!_psReadOnlyCommands.Contains(exe) || exe is "foreach-object" or "%" or "?")
            {
                return new(CommandRisk.RequiresApproval, $"'{name}' is not on the read-only list.");
            }

            if (_restrictedTools.TryGetValue(exe, out var allowed) && !allowed.IsMatch(segment.Trim()))
            {
                return new(CommandRisk.RequiresApproval, $"'{name}' is only read-only with specific arguments.");
            }
        }

        return new(CommandRisk.ReadOnly, "All statements are read-only commands.");
    }

    private static CommandClassification ClassifyBash(string command)
    {
        if (BashDangerousRegex().IsMatch(command))
        {
            return new(CommandRisk.RequiresApproval, "Contains redirection, substitution, sudo or command chaining.");
        }

        foreach (var segment in SplitStatements(command, ['|', '\n', '\r']))
        {
            var trimmed = segment.Trim();
            var name = FirstToken(trimmed);
            if (name is null)
            {
                continue;
            }

            if (!_bashReadOnlyCommands.Contains(name))
            {
                return new(CommandRisk.RequiresApproval, $"'{name}' is not on the read-only list.");
            }

            if (_bashRestrictedTools.TryGetValue(name, out var allowed) && !allowed.IsMatch(trimmed))
            {
                return new(CommandRisk.RequiresApproval, $"'{name}' is only read-only with specific arguments.");
            }

            // These read-only tools have flags that execute commands or write files.
            if ((name is "awk" && (AwkUnsafeRegex().IsMatch(trimmed) || AwkFileFlagRegex().IsMatch(trimmed))) ||
                (name is "find" or "file" && trimmed.Contains("-exec", StringComparison.Ordinal)) ||
                (name is "env" && trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length > 1) ||
                (name is "tail" or "journalctl" or "dmesg" or "top" &&
                    FollowFlagRegex().IsMatch(trimmed)))
            {
                return new(CommandRisk.RequiresApproval, $"'{name}' is used with arguments that aren't read-only or never exit.");
            }
        }

        return new(CommandRisk.ReadOnly, "All statements are read-only commands.");
    }

    private static IEnumerable<string> SplitStatements(string command, char[] separators)
    {
        return command
            .Split(separators, StringSplitOptions.RemoveEmptyEntries)
            .Select(x => x.Trim())
            .Where(x => x.Length > 0);
    }

    private static string? FirstToken(string segment)
    {
        var trimmed = segment.Trim();
        if (trimmed.Length == 0)
        {
            return null;
        }
        var end = trimmed.IndexOfAny([' ', '\t']);
        return end < 0 ? trimmed : trimmed[..end];
    }

    private static string StripExe(string name)
    {
        return name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? name[..^4] : name;
    }

    // Redirection, sub-expressions, script blocks, call operator, dynamic invocation,
    // encoded commands, variables (which could hold code) and backtick escapes.
    // Parentheses: "Get-Item (Remove-Item x)" runs the inner command, and "(...).Method()"
    // calls methods.  -Wait (Get-Content -Wait) never returns.
    [GeneratedRegex(@"[<>`()]|\$\(|\$\{|@\(|\{|\}|(^|[\s;|])&|&&|\|\||\binvoke-|\biex\b|\bstart-process\b|-enc|-encodedcommand|\[scriptblock\]|\.invoke\(|\badd-type\b|\bnew-object\b|\$\w+\s*=|::\w+\(|\s-wait\b", RegexOptions.IgnoreCase)]
    private static partial Regex PsDangerousRegex();

    // Verb prefixes that look read-only but aren't (e.g. Get-Credential prompts, Find-Package can install).
    // Network cmdlets reach a host chosen by the model, so they could carry data off the device.
    [GeneratedRegex(@"^(get-credential|test-computersecurechannel|test-netconnection|test-connection|resolve-dnsname|test-wsman|get-wsmaninstance)$", RegexOptions.IgnoreCase)]
    private static partial Regex PsNeverReadOnlyRegex();

    [GeneratedRegex(@"\s-(computername|cn|cimsession|session|pssession)\b", RegexOptions.IgnoreCase)]
    private static partial Regex PsRemoteTargetRegex();

    [GeneratedRegex(@"[<>&^%!()]|\|\|")]
    private static partial Regex CmdDangerousRegex();

    [GeneratedRegex(@"[<>`;&$(){}\\]|\bsudo\b|\bxargs\b|\btee\b")]
    private static partial Regex BashDangerousRegex();

    [GeneratedRegex(@"system\s*\(|getline|print\s*>|\|\s*""")]
    private static partial Regex AwkUnsafeRegex();

    // A program file could call system().
    [GeneratedRegex(@"(^|\s)(-f|--file)(\s|=|$)")]
    private static partial Regex AwkFileFlagRegex();

    [GeneratedRegex(@"\s/(o|t|output|temporary)\b", RegexOptions.IgnoreCase)]
    private static partial Regex CmdSortWriteRegex();

    // Password hashes, private keys, cloud credentials, the SAM hive, Wi-Fi keys.
    [GeneratedRegex(@"/etc/(g?shadow|sudoers)|\.ssh|id_(rsa|ecdsa|ed25519)|\.aws|\.kube|\.docker/config|\\config\\(sam|security|system)\b|\bhklm:?\\(sam|security)\b|\bhkey_local_machine\\(sam|security)\b|key\s*=\s*clear|\bntds\.dit\b|\bunattend\.xml\b", RegexOptions.IgnoreCase)]
    private static partial Regex SensitiveDataRegex();

    [GeneratedRegex(@"(^|\s)(-f|--follow(=\S*)?|-F)(\s|$)")]
    private static partial Regex FollowFlagRegex();

    [GeneratedRegex(@"^sc(\.exe)?\s+(query|queryex|qc|qdescription|qfailure|qtriggerinfo|enumdepend|getdisplayname|getkeyname)\b", RegexOptions.IgnoreCase)]
    private static partial Regex ReadOnlyScRegex();

    [GeneratedRegex(@"^reg(\.exe)?\s+query\b", RegexOptions.IgnoreCase)]
    private static partial Regex ReadOnlyRegRegex();

    // No "exec" (runs a script), no remote/script switches, no "key=clear" (Wi-Fi passwords).
    [GeneratedRegex(@"^netsh(\.exe)?\s+(?!-)(?!.*(\bexec\b|\s-[rfuap]\b|key\s*=\s*clear))(\S+\s+)*show\b", RegexOptions.IgnoreCase)]
    private static partial Regex ReadOnlyNetshRegex();

    [GeneratedRegex(@"^wevtutil(\.exe)?\s+(qe|query-events|el|enum-logs|gl|get-log|gli|get-log-info|ep|enum-publishers)\b", RegexOptions.IgnoreCase)]
    private static partial Regex ReadOnlyWevtutilRegex();

    [GeneratedRegex(@"^dism(\.exe)?\s+/online\s+(/get-\S+|/cleanup-image\s+/(checkhealth|scanhealth))(\s|$)", RegexOptions.IgnoreCase)]
    private static partial Regex ReadOnlyDismRegex();

    [GeneratedRegex(@"^chkdsk(\.exe)?(\s+[a-z]:)?\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex ReadOnlyChkdskRegex();

    [GeneratedRegex(@"^fsutil(\.exe)?\s+(fsinfo|volume\s+diskfree|dirty\s+query)\b", RegexOptions.IgnoreCase)]
    private static partial Regex ReadOnlyFsutilRegex();

    [GeneratedRegex(@"^w32tm(\.exe)?\s+/query\b", RegexOptions.IgnoreCase)]
    private static partial Regex ReadOnlyW32tmRegex();

    [GeneratedRegex(@"^powercfg(\.exe)?\s+(/list|/l|/query|/q|/getactivescheme|/a|/availablesleepstates|/requests|/lastwake)(\s|$)", RegexOptions.IgnoreCase)]
    private static partial Regex ReadOnlyPowercfgRegex();

    [GeneratedRegex(@"^manage-bde(\.exe)?\s+-status\b", RegexOptions.IgnoreCase)]
    private static partial Regex ReadOnlyManageBdeRegex();

    // No switches at all: /format:<url> runs a remote XSL script, /node: targets other machines.
    [GeneratedRegex(@"^wmic(\.exe)?\s+\w+(\s+(get|list)(\s+[\w,]+)*)?\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex ReadOnlyWmicRegex();

    // "net start" without arguments lists services; with a name it starts one.
    // Switches (/add, /delete, /minpwlen:...) always change something, so none are allowed.
    // "net use \\\\host" connects out and "net share x=C:\\" creates a share, so only the
    // listing forms of use/share/view/session are allowed.
    [GeneratedRegex(@"^net(\.exe)?\s+((start|accounts|share|use|session|view|statistics(\s+(workstation|server))?|config(\s+(workstation|server))?)|(user|localgroup)(\s+[^/\s=]+)?)\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex ReadOnlyNetRegex();

    [GeneratedRegex(@"^route(\.exe)?\s+print\b", RegexOptions.IgnoreCase)]
    private static partial Regex ReadOnlyRouteRegex();

    [GeneratedRegex(@"^query(\.exe)?\s+(user|session|process)\b", RegexOptions.IgnoreCase)]
    private static partial Regex ReadOnlyQueryRegex();
}
