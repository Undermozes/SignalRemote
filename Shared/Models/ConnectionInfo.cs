#nullable enable
namespace Remotely.Shared.Models;

public class ConnectionInfo
{
    private string? _host;

    public string DeviceID { get; set; } = Guid.NewGuid().ToString();
    public string? Host
    {
        get
        {
            return _host?.Trim()?.TrimEnd('/');
        }
        set
        {
            _host = value?.Trim()?.TrimEnd('/');
        }
    }
    public string? OrganizationID { get; set; }
    public string? ServerVerificationToken { get; set; }

    /// <summary>
    /// Secret the agent authenticates with.  Generated on first connect.
    /// </summary>
    public string? DeviceSecret { get; set; }

}
