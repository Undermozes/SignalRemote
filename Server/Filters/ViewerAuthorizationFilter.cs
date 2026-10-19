using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Remotely.Server.Services;

namespace Remotely.Server.Filters;

internal class ViewerAuthorizationFilter(
    IDataService _dataService,
    IOtpProvider _otpProvider):  IAsyncAuthorizationFilter
{
    public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        if (await IsAuthorized(context))
        {
            return;
        }

        context.Result = new RedirectResult("/Account/Login");
    }

    private async Task<bool> IsAuthorized(AuthorizationFilterContext context)
    {
        // Remote control is never public: a signed-in user, or a one-time link (OTP)
        // created through the authenticated API.  RemoteControlRequiresAuthentication
        // is no longer used to open it up.
        await Task.CompletedTask;
        if (context.HttpContext.User.Identity?.IsAuthenticated == true)
        {
            return true;
        }

        if (context.HttpContext.Request.Query.TryGetValue("otp", out var otp) &&
            _otpProvider.Exists($"{otp}"))
        {
            return true;
        }

        return false;
    }
}
