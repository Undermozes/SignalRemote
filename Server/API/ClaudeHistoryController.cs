using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Remotely.Server.Auth;
using Remotely.Server.Services;
using Remotely.Server.Services.ClaudeAssistant;
using System.Globalization;
using System.Text;

namespace Remotely.Server.API;

/// <summary>
/// Screenshots and CSV exports from the Claude assistant history.  Admins see their whole
/// organization, others only their own sessions.
/// </summary>
[Route("api/claude-history")]
[ApiController]
[Authorize(Policy = PolicyNames.TwoFactorRequired)]
public class ClaudeHistoryController : ControllerBase
{
    private readonly IDataService _dataService;
    private readonly IClaudeAssistantHistory _history;

    public ClaudeHistoryController(IDataService dataService, IClaudeAssistantHistory history)
    {
        _dataService = dataService;
        _history = history;
    }

    [HttpGet("screenshot/{eventId:guid}")]
    public async Task<IActionResult> GetScreenshot(Guid eventId)
    {
        if (string.IsNullOrWhiteSpace(User.Identity?.Name))
        {
            return Unauthorized();
        }

        var userResult = await _dataService.GetUserByName(User.Identity.Name);
        if (!userResult.IsSuccess)
        {
            return Unauthorized();
        }

        var user = userResult.Value;
        var bytes = await _history.GetScreenshot(
            user.OrganizationID,
            user.IsAdministrator ? null : user.UserName,
            eventId);

        if (bytes is null || bytes.Length == 0)
        {
            return NotFound();
        }

        Response.Headers.CacheControl = "private, max-age=86400";
        return File(bytes, "image/jpeg");
    }

    /// <summary>
    /// Sessions in a date range as CSV (for billing customers), with tokens and cost.
    /// Dates are days in the server's time zone; "to" is inclusive.  Uses ";" and a
    /// decimal comma so Excel in Belgium/Netherlands opens it directly.
    /// </summary>
    [HttpGet("export")]
    public async Task<IActionResult> Export([FromQuery] DateOnly from, [FromQuery] DateOnly to)
    {
        if (string.IsNullOrWhiteSpace(User.Identity?.Name))
        {
            return Unauthorized();
        }

        var userResult = await _dataService.GetUserByName(User.Identity.Name);
        if (!userResult.IsSuccess)
        {
            return Unauthorized();
        }

        if (to < from || to.DayNumber - from.DayNumber > 366)
        {
            return BadRequest("Choose a range of at most one year.");
        }

        var user = userResult.Value;
        var zone = TimeZoneInfo.Local;
        var fromUtc = ToUtc(from, zone);
        var toUtc = ToUtc(to.AddDays(1), zone);

        var sessions = await _history.GetSessionsInRange(
            user.OrganizationID,
            user.IsAdministrator ? null : user.UserName,
            fromUtc,
            toUtc);

        // Decimal comma without relying on culture data being installed in the container.
        var culture = new NumberFormatInfo { NumberDecimalSeparator = ",", NumberGroupSeparator = "" };
        var csv = new StringBuilder();
        csv.AppendLine(string.Join(';',
            "Start", "Device", "Technician", "Question", "Commands", "Screenshots",
            "Input tokens", "Output tokens", "Cache read tokens", "Cache write tokens", "Cost (USD)", "Session ID"));

        foreach (var session in sessions)
        {
            csv.AppendLine(string.Join(';',
                Cell(TimeZoneInfo.ConvertTime(session.Started, zone).ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)),
                Cell(session.DeviceName ?? session.DeviceId),
                Cell(session.UserName),
                Cell(session.FirstQuestion),
                session.Commands.ToString(CultureInfo.InvariantCulture),
                session.Screenshots.ToString(CultureInfo.InvariantCulture),
                session.Usage.Input.ToString(CultureInfo.InvariantCulture),
                session.Usage.Output.ToString(CultureInfo.InvariantCulture),
                session.Usage.CacheRead.ToString(CultureInfo.InvariantCulture),
                session.Usage.CacheWrite.ToString(CultureInfo.InvariantCulture),
                session.Usage.CostUsd.ToString("0.0000", culture),
                session.SessionId.ToString()));
        }

        var total = sessions.Sum(x => x.Usage.CostUsd);
        csv.AppendLine(string.Join(';', "Total", "", "", "", "", "", "", "", "", "", total.ToString("0.0000", culture), ""));

        // UTF-8 with BOM, so Excel shows accented characters correctly.
        var bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(csv.ToString())).ToArray();
        return File(bytes, "text/csv; charset=utf-8", $"claude-history-{from:yyyy-MM-dd}-{to:yyyy-MM-dd}.csv");
    }

    private static DateTimeOffset ToUtc(DateOnly day, TimeZoneInfo zone)
    {
        var local = day.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        return new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(local, zone), TimeSpan.Zero);
    }

    private static string Cell(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return "";
        }

        var text = value.Replace("\r", " ").Replace("\n", " ");
        // Keep spreadsheet apps from treating text as a formula.
        if (text[0] is '=' or '+' or '-' or '@')
        {
            text = "'" + text;
        }
        return $"\"{text.Replace("\"", "\"\"")}\"";
    }
}
