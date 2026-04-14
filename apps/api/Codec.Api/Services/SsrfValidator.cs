using System.Net;
using System.Net.Sockets;

namespace Codec.Api.Services;

/// <summary>
/// Validates IP addresses to prevent Server-Side Request Forgery (SSRF).
/// Blocks connections to private, loopback, and link-local addresses.
/// </summary>
public static class SsrfValidator
{
    /// <summary>
    /// Returns true if the IP address is in a private, loopback, or link-local range.
    /// </summary>
    public static bool IsPrivateOrReserved(IPAddress ip)
    {
        if (IPAddress.IsLoopback(ip))
            return true;

        if (ip.AddressFamily == AddressFamily.InterNetwork)
        {
            var bytes = ip.GetAddressBytes();
            return bytes[0] switch
            {
                10 => true,                                // 10.0.0.0/8
                172 => bytes[1] >= 16 && bytes[1] <= 31,   // 172.16.0.0/12
                192 => bytes[1] == 168,                     // 192.168.0.0/16
                169 => bytes[1] == 254,                     // 169.254.0.0/16 link-local
                127 => true,                                // 127.0.0.0/8
                0 => true,                                  // 0.0.0.0/8
                _ => false
            };
        }

        if (ip.AddressFamily == AddressFamily.InterNetworkV6)
        {
            if (ip.IsIPv6LinkLocal || ip.IsIPv6SiteLocal)
                return true;

            var bytes = ip.GetAddressBytes();
            if (bytes[0] is 0xFC or 0xFD)
                return true;
        }

        return false;
    }

    /// <summary>
    /// Creates a ConnectCallback that validates resolved IPs against SSRF before connecting.
    /// </summary>
    public static Func<SocketsHttpConnectionContext, CancellationToken, ValueTask<Stream>> CreateSafeConnectCallback(string label)
    {
        return async (context, cancellationToken) =>
        {
            var entries = await Dns.GetHostAddressesAsync(context.DnsEndPoint.Host, cancellationToken);
            var safeEntries = new List<IPAddress>(entries.Length);
            foreach (var entry in entries)
            {
                var ip = entry.IsIPv4MappedToIPv6 ? entry.MapToIPv4() : entry;

                if (IsPrivateOrReserved(ip))
                    throw new HttpRequestException($"Blocked {label} connection to private IP {ip}.");

                safeEntries.Add(entry);
            }

            // Try each validated address for failover when the first is unreachable.
            for (var i = 0; i < safeEntries.Count; i++)
            {
                var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
                try
                {
                    await socket.ConnectAsync(new IPEndPoint(safeEntries[i], context.DnsEndPoint.Port), cancellationToken);
                    return new NetworkStream(socket, ownsSocket: true);
                }
                catch
                {
                    socket.Dispose();
                    if (i == safeEntries.Count - 1)
                        throw;
                }
            }

            throw new HttpRequestException($"No addresses resolved for {context.DnsEndPoint.Host}.");
        };
    }
}
