using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
namespace Browser.Core;
public static class LanPreview
{
    public static bool IsLocal(string url) => Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https"
        && uri.UserInfo.Length == 0 && (uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
        || IPAddress.TryParse(uri.Host.Trim('[', ']'), out var ip) && IPAddress.IsLoopback(ip));
    public static string? Rewrite(string url, string address) => IsLocal(url) && IPAddress.TryParse(address, out var ip)
        && ip.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(ip) && !ip.Equals(IPAddress.Any)
        ? new UriBuilder(url) { Host = ip.ToString() }.Uri.AbsoluteUri : null;
    public static IReadOnlyList<(string Name, string Address)> Addresses() => NetworkInterface.GetAllNetworkInterfaces()
        .Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType != NetworkInterfaceType.Loopback)
        .SelectMany(n => n.GetIPProperties().UnicastAddresses.Where(a => a.Address.AddressFamily == AddressFamily.InterNetwork
            && !IPAddress.IsLoopback(a.Address) && !a.Address.ToString().StartsWith("169.254."))
            .Select(a => (n.Name, a.Address.ToString()))).Distinct().ToArray();
}
