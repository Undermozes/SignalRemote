using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Remotely.Server.Services;

namespace Remotely.Server.Filters;

/// <summary>
/// Installers, desktop clients and the Android setup page are only for signed-in users,
/// or for whoever holds a valid time-limited link for the organization in the route
/// (see <see cref="IDownloadLinkService"/>).
/// </summary>
public class DownloadAccessFilter(IDownloadLinkService _downloadLinks) : IAuthorizationFilter
{
    public void OnAuthorization(AuthorizationFilterContext context)
    {
        var httpContext = context.HttpContext;
        if (httpContext.User.Identity?.IsAuthenticated == true)
        {
            return;
        }

        var organizationId = context.RouteData.Values.TryGetValue("organizationId", out var routeOrg)
            ? $"{routeOrg}"
            : string.Empty;
        var token = httpContext.Request.Query[DownloadLinkService.QueryKey].ToString();

        if (_downloadLinks.IsValid(token, organizationId))
        {
            // Links can be shared; make sure no proxy or browser cache keeps the response.
            httpContext.Response.Headers.CacheControl = "no-store";
            return;
        }

        context.Result = new ContentResult
        {
            StatusCode = StatusCodes.Status403Forbidden,
            ContentType = "text/plain; charset=utf-8",
            Content = "This download link is invalid or has expired. Ask your IT provider for a new link.",
        };
    }
}
