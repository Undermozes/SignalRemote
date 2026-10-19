using Microsoft.AspNetCore.DataProtection;
using System.Security.Cryptography;

namespace Remotely.Server.Services;

/// <summary>
/// Time-limited download links.  Installers and clients are never public: a signed-in
/// technician downloads them directly, or creates a link that works for a limited time
/// (e.g. to send to a customer).  Tokens are signed with the app's data protection keys
/// (persisted in AppData), so they survive restarts and need no database.
/// </summary>
public interface IDownloadLinkService
{
    string CreateToken(string organizationId, TimeSpan lifetime);

    /// <summary>True when the token is valid, not expired, and issued for this organization.</summary>
    bool IsValid(string? token, string organizationId);
}

public class DownloadLinkService : IDownloadLinkService
{
    public const string QueryKey = "t";

    private readonly ITimeLimitedDataProtector _protector;

    public DownloadLinkService(IDataProtectionProvider provider)
    {
        _protector = provider
            .CreateProtector("SignalRemote.DownloadLinks.v1")
            .ToTimeLimitedDataProtector();
    }

    public string CreateToken(string organizationId, TimeSpan lifetime)
    {
        return _protector.Protect(organizationId, lifetime);
    }

    public bool IsValid(string? token, string organizationId)
    {
        if (string.IsNullOrWhiteSpace(token) || string.IsNullOrWhiteSpace(organizationId))
        {
            return false;
        }

        try
        {
            var tokenOrganizationId = _protector.Unprotect(token);
            return string.Equals(tokenOrganizationId, organizationId, StringComparison.OrdinalIgnoreCase);
        }
        catch (CryptographicException)
        {
            // Tampered, expired, or signed with other keys.
            return false;
        }
    }
}
