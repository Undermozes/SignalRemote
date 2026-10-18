using Microsoft.AspNetCore.SignalR;
using Remotely.Server.Services;

namespace Remotely.Server.Hubs;

/// <summary>
/// Enforces "Require Authentication on Remote Control Page" on the viewer hub itself.
/// The MVC filter on the Viewer page doesn't apply to SignalR, so without this the hub
/// could be used directly without signing in.
/// </summary>
public class ViewerHubAuthFilter : IHubFilter
{
    private readonly ILogger<ViewerHubAuthFilter> _logger;

    public ViewerHubAuthFilter(ILogger<ViewerHubAuthFilter> logger)
    {
        _logger = logger;
    }

    public async Task OnConnectedAsync(HubLifetimeContext context, Func<HubLifetimeContext, Task> next)
    {
        if (context.Hub is ViewerHub && !await IsAuthorized(context))
        {
            _logger.LogWarning(
                "Rejected unauthenticated viewer connection from {ip}.",
                context.Context.GetHttpContext()?.Connection.RemoteIpAddress);
            context.Context.Abort();
            return;
        }

        await next(context);
    }

    private static async Task<bool> IsAuthorized(HubLifetimeContext context)
    {
        var dataService = context.ServiceProvider.GetRequiredService<IDataService>();
        var settings = await dataService.GetSettings();
        if (!settings.RemoteControlRequiresAuthentication)
        {
            return true;
        }

        if (context.Context.User?.Identity?.IsAuthenticated == true)
        {
            return true;
        }

        var otp = context.Context.GetHttpContext()?.Request.Query["otp"].ToString();
        if (!string.IsNullOrWhiteSpace(otp))
        {
            var otpProvider = context.ServiceProvider.GetRequiredService<IOtpProvider>();
            return otpProvider.Exists(otp);
        }

        return false;
    }
}
