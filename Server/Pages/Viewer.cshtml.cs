using Remotely.Server.Filters;
using Remotely.Server.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Remotely.Server.Services;
using Remotely.Server.Services.ClaudeAssistant;

namespace Remotely.Server.Pages;

[ServiceFilter(typeof(ViewerAuthorizationFilter))]
public class ViewerModel(
    IDataService _dataService,
    IRemoteControlSessionCache _sessionCache,
    IClaudeAssistantService _assistantService) : PageModel
{
    /// <summary>
    /// The device of this unattended session, when the Claude assistant panel is available.
    /// </summary>
    public string AssistantDeviceId { get; private set; } = string.Empty;
    public string FaviconUrl { get; } = "favicon.ico";
    public string LogoUrl { get; set; } = string.Empty;
    public string PageDescription { get; } = "Open-source remote support tools.";
    public string PageTitle { get; } = "Remotely Remote Control";
    public string ThemeUrl { get; private set; } = string.Empty;
    public string UserDisplayName { get; private set; } = string.Empty;

    public async Task OnGet()
    {
        var theme = await GetTheme();

        ThemeUrl = theme switch
        {
            ViewerPageTheme.Dark => "/css/remote-control-dark.css",
            ViewerPageTheme.Light => "/css/remote-control-light.css",
            _ => "/css/remote-control-dark.css"
        };
        UserDisplayName = await GetUserDisplayName();
        LogoUrl = await GetLogoUrl();
        AssistantDeviceId = await GetAssistantDeviceId();
    }

    private async Task<string> GetAssistantDeviceId()
    {
        if (User?.Identity?.IsAuthenticated != true ||
            string.IsNullOrWhiteSpace(User.Identity.Name) ||
            !await _assistantService.IsEnabled())
        {
            return string.Empty;
        }

        // Only unattended sessions are tied to a device.
        var sessionId = Request.Query["sessionId"].ToString();
        if (string.IsNullOrWhiteSpace(sessionId) ||
            !_sessionCache.TryGetValue(sessionId, out var session) ||
            string.IsNullOrWhiteSpace(session.DeviceId))
        {
            return string.Empty;
        }

        // The assistant page checks access again; this only decides whether to show the button.
        var userResult = await _dataService.GetUserByName(User.Identity.Name);
        if (!userResult.IsSuccess ||
            !_dataService.DoesUserHaveAccessToDevice(session.DeviceId, userResult.Value))
        {
            return string.Empty;
        }

        return session.DeviceId;
    }

    private async Task<string> GetLogoUrl()
    {
        return await GetTheme() == ViewerPageTheme.Dark ?
           "/images/viewer/remotely-logo-dark.svg" :
           "/images/viewer/remotely-logo-light.svg";
    }

    private Task<ViewerPageTheme> GetTheme()
    {
        // TODO: Implement light theme in new viewer design.
        return Task.FromResult(ViewerPageTheme.Dark);
        //if (User.Identity.IsAuthenticated)
        //{
        //    var user = _dataService.GetUserByNameWithOrg(User.Identity.Name);

        //    var userTheme = user.UserOptions.Theme switch
        //    {
        //        Theme.Light => ViewerPageTheme.Light,
        //        Theme.Dark => ViewerPageTheme.Dark,
        //        _ => ViewerPageTheme.Dark
        //    };
        //    return Task.FromResult(userTheme);
        //}

        //var appTheme = _appConfig.Theme switch
        //{
        //    Theme.Light => ViewerPageTheme.Light,
        //    Theme.Dark => ViewerPageTheme.Dark,
        //    _ => ViewerPageTheme.Dark
        //};
        //return Task.FromResult(appTheme);
    }

    private async Task<string> GetUserDisplayName()
    {
        if (string.IsNullOrWhiteSpace(User?.Identity?.Name))
        {
            return string.Empty;
        }

        var userResult = await _dataService.GetUserByName(User.Identity.Name);

        if (!userResult.IsSuccess)
        {
            return string.Empty;
        }

        var user = userResult.Value;
        var displayName = user.UserOptions?.DisplayName ?? user.UserName ?? string.Empty;
        return displayName;
    }
}
