using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Remotely.Server.Extensions;
using Remotely.Server.Services;
using Remotely.Shared;
using Remotely.Shared.Utilities;
using System.Net;

namespace Remotely.Server.Auth;

public class ExpiringTokenFilter : ActionFilterAttribute, IAsyncAuthorizationFilter
{
    private readonly IDataService _dataService;
    private readonly IExpiringTokenService _expiringTokenService;
    private readonly ILogger<ExpiringTokenFilter> _logger;

    public ExpiringTokenFilter(
        IExpiringTokenService expiringTokenService,
        IDataService dataService,
        ILogger<ExpiringTokenFilter> logger)
    {
        _dataService = dataService;
        _expiringTokenService = expiringTokenService;
        _logger = logger;
    }

    public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        try
        {
            await Authorize(context);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error while authorizing expiring token.");
            // Fail closed.  Never let a request through when authorization threw.
            context.HttpContext.Request.Headers.ResetAuthHeaders();
            context.Result = new UnauthorizedResult();
        }
    }

    private async Task Authorize(AuthorizationFilterContext context)
    {
        var http = context.HttpContext;
        http.Request.Headers.ResetAuthHeaders();

        if (http.User.Identity?.IsAuthenticated == true)
        {
            var userResult = await _dataService.GetUserByName($"{http.User.Identity.Name}");
            if (!userResult.IsSuccess)
            {
                http.Response.StatusCode = (int)HttpStatusCode.Forbidden;
                context.Result = new UnauthorizedResult();
                return;
            }

            http.Request.Headers["OrganizationID"] = userResult.Value.OrganizationID;
            return;
        }

        if (http.Request.Headers.TryGetValue(AppConstants.ApiKeyHeaderName, out var apiHeaderValue))
        {
            var headerComponents = apiHeaderValue.ToString().Split(":");
            if (headerComponents.Length < 2)
            {
                http.Response.StatusCode = (int)HttpStatusCode.Forbidden;
                context.Result = new UnauthorizedResult();
                return;
            };

            var keyId = headerComponents[0].Trim();
            var secret = headerComponents[1].Trim();

            var isValid = await _dataService.ValidateApiKey(
                       keyId,
                       secret,
                       http.Request.Path,
                       $"{http.Connection.RemoteIpAddress}");

            if (isValid)
            {
                var keyResult = await _dataService.GetApiKey(keyId);

                if (keyResult.IsSuccess)
                {
                    _logger.LogDebug("Expiring token authorized via API key.  Key ID: {keyId}.", keyId);
                    http.Request.Headers["OrganizationID"] = keyResult.Value.OrganizationID;
                    return;
                }
            }
        }

        if (http.Request.Headers.TryGetValue(AppConstants.ExpiringTokenHeaderName, out var expiringToken))
        {
            if (_expiringTokenService.TryGetToken(expiringToken.ToString(), out var token) &&
                token.Expiration > Time.Now)
            {
                _logger.LogDebug(
                    "Expiring token authorized.  Device: {deviceId}.  Expiration: {expiration}",
                    token.DeviceId,
                    token.Expiration);
                http.Request.Headers["OrganizationID"] = token.OrganizationId;
                http.Request.Headers[IHeaderDictionaryExtensions.TokenDeviceIdHeader] = token.DeviceId;
                return;
            }
        }

        _logger.LogDebug("Expiring token authorization failed.");
        http.Response.StatusCode = (int)HttpStatusCode.Forbidden;
        context.Result = new UnauthorizedResult();
    }
}
