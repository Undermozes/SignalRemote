using System.Diagnostics.CodeAnalysis;
using Remotely.Server.Auth;

namespace Remotely.Server.Extensions;

public static class IHeaderDictionaryExtensions
{
    /// <summary>
    /// Set by <see cref="ExpiringTokenFilter"/> to the device ID that the expiring
    /// token was issued for.  Any client-supplied value is overwritten by the filters.
    /// </summary>
    public const string TokenDeviceIdHeader = "X-Remotely-Token-DeviceId";

    /// <summary>
    /// If true, the request was authorized via an expiring token that was issued
    /// to the returned device.
    /// </summary>
    public static bool TryGetTokenDeviceId(
        this IHeaderDictionary headers,
        [NotNullWhen(true)] out string? deviceId)
    {
        deviceId = null;
        if (!headers.TryGetValue(TokenDeviceIdHeader, out var value) ||
            string.IsNullOrWhiteSpace(value))
        {
            return false;
        }
        deviceId = $"{value}";
        return true;
    }

    /// <summary>
    /// Clears the server-controlled auth headers so client-supplied values are never trusted.
    /// </summary>
    public static void ResetAuthHeaders(this IHeaderDictionary headers)
    {
        headers["OrganizationID"] = string.Empty;
        headers[TokenDeviceIdHeader] = string.Empty;
    }

    /// <summary>
    /// If true, ensures that the user was authorized via <see cref="ApiAuthorizationFilter"/>,
    /// and is either an Administrator or is using a valid API access token.
    /// </summary>
    /// <param name="headers"></param>
    /// <param name="organizationId"></param>
    /// <returns></returns>
    public static bool TryGetOrganizationId(
        this IHeaderDictionary headers, 
        [NotNullWhen(true)] out string? organizationId)
    {
        organizationId = null;

        if (!headers.TryGetValue("OrganizationId", out var orgId))
        {
            return false;
        }

        organizationId = $"{orgId}";
        
        if (string.IsNullOrWhiteSpace(organizationId))
        {
            return false;
        }

        return true;
    }
}
