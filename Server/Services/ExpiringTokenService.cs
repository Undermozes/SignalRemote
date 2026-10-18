using Remotely.Shared.Helpers;
using Microsoft.Extensions.Caching.Memory;
using System.Diagnostics.CodeAnalysis;

namespace Remotely.Server.Services;

/// <summary>
/// A short-lived token issued to an agent.  The token is bound to the organization
/// and device it was issued for, so it can't be used to access another
/// organization's data or to impersonate another device.
/// </summary>
public record ExpiringToken(DateTimeOffset Expiration, string OrganizationId, string DeviceId);

public interface IExpiringTokenService
{
    string GetToken(DateTimeOffset expiration, string organizationId, string deviceId);
    bool TryGetToken(string secret, [NotNullWhen(true)] out ExpiringToken? token);
}

public class ExpiringTokenService : IExpiringTokenService
{
    private static readonly MemoryCache _tokenCache = new(new MemoryCacheOptions());

    public string GetToken(DateTimeOffset expiration, string organizationId, string deviceId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(organizationId);
        ArgumentException.ThrowIfNullOrWhiteSpace(deviceId);

        var secret = RandomGenerator.GenerateString(36);
        _tokenCache.Set(secret, new ExpiringToken(expiration, organizationId, deviceId), expiration);
        return secret;
    }

    public bool TryGetToken(string secret, [NotNullWhen(true)] out ExpiringToken? token)
    {
        if (string.IsNullOrWhiteSpace(secret))
        {
            token = null;
            return false;
        }
        return _tokenCache.TryGetValue(secret, out token) && token is not null;
    }
}
