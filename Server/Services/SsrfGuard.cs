using System.Net;
using System.Net.Sockets;

namespace Remotely.Server.Services;

/// <summary>
/// Prevents server-side request forgery for outbound requests whose URL is supplied
/// by API callers (e.g. alert webhooks).  Requests may only go to public addresses.
/// The check runs on the resolved IP at connect time, so DNS rebinding can't bypass it.
/// </summary>
public static class SsrfGuard
{
    public const string HttpClientName = "PublicOnly";

    public static bool IsAllowedUrl(string? url, out Uri? uri)
    {
        uri = null;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var parsed))
        {
            return false;
        }
        if (parsed.Scheme != Uri.UriSchemeHttps && parsed.Scheme != Uri.UriSchemeHttp)
        {
            return false;
        }
        if (IPAddress.TryParse(parsed.DnsSafeHost, out var literal) && !IsPublicAddress(literal))
        {
            return false;
        }
        uri = parsed;
        return true;
    }

    public static SocketsHttpHandler CreateHandler()
    {
        return new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            ConnectTimeout = TimeSpan.FromSeconds(10),
            ConnectCallback = async (context, cancellationToken) =>
            {
                var addresses = await Dns.GetHostAddressesAsync(context.DnsEndPoint.Host, cancellationToken);
                var target = addresses.FirstOrDefault(IsPublicAddress)
                    ?? throw new HttpRequestException(
                        $"Requests to {context.DnsEndPoint.Host} are not allowed (non-public address).");

                if (addresses.Any(x => !IsPublicAddress(x)))
                {
                    throw new HttpRequestException(
                        $"Requests to {context.DnsEndPoint.Host} are not allowed (resolves to a non-public address).");
                }

                var socket = new Socket(target.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
                try
                {
                    await socket.ConnectAsync(new IPEndPoint(target, context.DnsEndPoint.Port), cancellationToken);
                    return new NetworkStream(socket, ownsSocket: true);
                }
                catch
                {
                    socket.Dispose();
                    throw;
                }
            }
        };
    }

    public static bool IsPublicAddress(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        if (IPAddress.IsLoopback(address) ||
            address.Equals(IPAddress.Any) ||
            address.Equals(IPAddress.IPv6Any) ||
            address.Equals(IPAddress.Broadcast))
        {
            return false;
        }

        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            var b = address.GetAddressBytes();
            return !(
                b[0] == 0 ||                                  // 0.0.0.0/8
                b[0] == 10 ||                                 // 10.0.0.0/8
                (b[0] == 100 && b[1] >= 64 && b[1] <= 127) || // 100.64.0.0/10 (CGNAT)
                b[0] == 127 ||                                // 127.0.0.0/8
                (b[0] == 169 && b[1] == 254) ||               // 169.254.0.0/16 (link-local, cloud metadata)
                (b[0] == 172 && b[1] >= 16 && b[1] <= 31) ||  // 172.16.0.0/12
                (b[0] == 192 && b[1] == 168) ||               // 192.168.0.0/16
                (b[0] == 192 && b[1] == 0 && b[2] == 0) ||    // 192.0.0.0/24
                (b[0] == 198 && (b[1] == 18 || b[1] == 19)) || // 198.18.0.0/15
                b[0] >= 224);                                 // multicast and reserved
        }

        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            if (address.IsIPv6LinkLocal || address.IsIPv6SiteLocal || address.IsIPv6Multicast)
            {
                return false;
            }
            var b = address.GetAddressBytes();
            // fc00::/7 unique local addresses
            if ((b[0] & 0xFE) == 0xFC)
            {
                return false;
            }
            return true;
        }

        return false;
    }
}
