using System.Net;
using System.Net.Sockets;

namespace IPBanManager.Core.Validation;

public static class IpEntry
{
    /// <summary>IP simples (v4/v6) ou faixa CIDR.</summary>
    public static bool IsIpOrCidr(string text) => TryParse(text, out _, out _);

    /// <summary>Entrada de whitelist/blacklist: IP, CIDR, DNS ou URL (sem '|', ',' ou espaços).</summary>
    public static bool IsListEntry(string text) =>
        !string.IsNullOrWhiteSpace(text) && text.IndexOfAny(new[] { '|', ',', ' ' }) < 0;

    public static bool IsLoopbackOrUnspecified(string text) =>
        TryParse(text, out var ip, out _) && (IPAddress.IsLoopback(ip) || ip.Equals(IPAddress.Any) || ip.Equals(IPAddress.IPv6Any));

    /// <summary>Faixa CIDR que cobre muitos endereços (IPv4 até /8, IPv6 até /16).</summary>
    public static bool IsWideRange(string text)
    {
        if (!TryParse(text, out var ip, out var prefix) || prefix == null) return false;
        return ip.AddressFamily == AddressFamily.InterNetwork ? prefix <= 8 : prefix <= 16;
    }

    private static bool TryParse(string text, out IPAddress ip, out int? prefix)
    {
        ip = null;
        prefix = null;
        if (string.IsNullOrWhiteSpace(text)) return false;
        var parts = text.Trim().Split('/');
        if (parts.Length > 2 || !IPAddress.TryParse(parts[0], out ip)) return false;
        // TryParse aceita formas legadas ("1.2.3"); exige IPv4 com 4 octetos.
        if (ip.AddressFamily == AddressFamily.InterNetwork && parts[0].Split('.').Length != 4) return false;
        if (parts.Length == 1) return true;
        int max = ip.AddressFamily == AddressFamily.InterNetwork ? 32 : 128;
        if (!int.TryParse(parts[1], out var p) || p < 0 || p > max) return false;
        prefix = p;
        return true;
    }
}
