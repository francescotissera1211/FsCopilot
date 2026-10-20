namespace FsCopilot;

using System.Net;

/// <summary>
/// Direct mode helpers (GrainLin-0507). The settings themselves (connection type and port) live
/// in Connection settings, <see cref="ConnectionConfig"/>, like every other connection setting;
/// --direct and --direct-port override them for one run.
/// </summary>
public static class NetworkConfig
{
    public const int DefaultDirectPort = 38510;

    /// <summary>This machine's IPv4 addresses, for the partner to type: what the main window shows.</summary>
    public static string LocalAddresses()
    {
        try
        {
            var list = Dns.GetHostAddresses(Dns.GetHostName())
                .Where(x => x.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                .Where(x => !IPAddress.IsLoopback(x))
                .Select(x => x.ToString())
                .ToArray();

            return list.Length > 0 ? string.Join(", ", list) : "127.0.0.1";
        }
        catch
        {
            return "127.0.0.1";
        }
    }
}
