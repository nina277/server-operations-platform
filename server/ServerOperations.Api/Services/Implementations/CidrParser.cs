using System.Net;
using System.Net.Sockets;

namespace ServerOperations.Api.Services.Implementations;

/// <summary>CIDR文字列の解析・正規化・包含判定ヘルパー。</summary>
public static class CidrParser
{
    /// <summary>
    /// CIDR文字列を解析し、ホストビットをマスクした正規形で返す(例: 192.168.1.5/24 → 192.168.1.0/24)。
    /// </summary>
    public static bool TryParse(string input, out IPNetwork network)
    {
        network = default;

        var parts = input.Trim().Split('/');
        if (parts.Length != 2)
        {
            return false;
        }

        if (!IPAddress.TryParse(parts[0], out var address) || !int.TryParse(parts[1], out var prefix))
        {
            return false;
        }

        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        var maxPrefix = address.AddressFamily switch
        {
            AddressFamily.InterNetwork => 32,
            AddressFamily.InterNetworkV6 => 128,
            _ => -1,
        };
        if (maxPrefix < 0 || prefix < 0 || prefix > maxPrefix)
        {
            return false;
        }

        network = new IPNetwork(Mask(address, prefix), prefix);
        return true;
    }

    /// <summary>IPがCIDR範囲内か判定する。IPv4射影IPv6アドレスはIPv4として扱う。</summary>
    public static bool Contains(IPNetwork network, IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        return network.BaseAddress.AddressFamily == address.AddressFamily && network.Contains(address);
    }

    private static IPAddress Mask(IPAddress address, int prefix)
    {
        var bytes = address.GetAddressBytes();
        for (var i = 0; i < bytes.Length; i++)
        {
            var bitsInThisByte = Math.Clamp(prefix - i * 8, 0, 8);
            bytes[i] &= (byte)(0xFF << (8 - bitsInThisByte));
        }

        return new IPAddress(bytes);
    }
}
