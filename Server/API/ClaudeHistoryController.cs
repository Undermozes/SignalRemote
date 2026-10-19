using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Remotely.Server.Auth;
using Remotely.Server.Services;
using Remotely.Server.Services.ClaudeAssistant;

namespace Remotely.Server.API;

/// <summary>
/// Serves screenshots from the Claude assistant history, so the history page can show
/// them as plain images.  Admins see their whole organization, others only their own sessions.
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
}
