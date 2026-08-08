using System.Net;
using System.Net.Sockets;

namespace ServerOperations.Core.Adapters.Implementations;

/// <summary>
/// 接続先URLの安全性検証。SSRF対策として、ループバック・リンクローカル(クラウドメタデータIP含む)・
/// マルチキャスト・未指定アドレスへの接続を拒否する。ホスト名はDNS解決後の全IPを検査する。
/// </summary>
public static class EndpointValidator
{
    /// <summary>HTTP/HTTPSヘルスチェックURLを検証する。</summary>
    public static async Task ValidateHttpUrlAsync(string url, CancellationToken ct = default)
    {
        var uri = ParseUri(url, ["http", "https"]);
        await ValidateHostAsync(uri, ct);
    }

    /// <summary>
    /// Docker接続先を検証する。Socket Proxy(http)またはTLS保護済みAPI(https)のみ許可し、
    /// unix://(ソケット直接マウント)等は拒否する。
    /// </summary>
    public static async Task ValidateDockerEndpointAsync(string endpoint, CancellationToken ct = default)
    {
        var uri = ParseUri(endpoint, ["http", "https"],
            "Docker接続先はSocket Proxy(http)またはTLS保護済みAPI(https)のURLだけを指定できます。");
        await ValidateHostAsync(uri, ct);
    }

    /// <summary>
    /// ホスト名とポートの組を検証する。SMTPのようにURLではない接続先に使う。
    /// URLと同じ基準(ループバック・リンクローカル等の遮断)を適用する。
    /// </summary>
    public static async Task ValidateHostPortAsync(
        string host, int port, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(host))
        {
            throw AppException.BadRequest("invalid_host", "接続先のホスト名を指定してください。");
        }

        if (port is < 1 or > 65535)
        {
            throw AppException.BadRequest("invalid_port", "ポート番号は1〜65535の範囲で指定してください。");
        }

        // ホスト名に別の情報を混ぜて渡されないようにする
        var trimmed = host.Trim();
        if (trimmed.Contains('/') || trimmed.Contains(':') && !IPAddress.TryParse(trimmed, out _))
        {
            throw AppException.BadRequest(
                "invalid_host", "接続先はホスト名またはIPアドレスだけを指定してください。");
        }

        await ValidateResolvedHostAsync(trimmed, ct);
    }

    private static Uri ParseUri(string value, string[] allowedSchemes, string? schemeMessage = null)
    {
        if (!Uri.TryCreate(value?.Trim(), UriKind.Absolute, out var uri))
        {
            throw AppException.BadRequest("invalid_url", "URLの形式が正しくありません。");
        }

        if (!allowedSchemes.Contains(uri.Scheme, StringComparer.OrdinalIgnoreCase))
        {
            throw AppException.BadRequest(
                "invalid_url_scheme",
                schemeMessage ?? "http または https のURLだけを指定できます。");
        }

        // URL埋め込みの認証情報は一覧APIで露出するため拒否する(Basic認証はcredentialsで指定する)
        if (!string.IsNullOrEmpty(uri.UserInfo))
        {
            throw AppException.BadRequest(
                "credentials_in_url",
                "URLに認証情報(user:pass@)を埋め込まないでください。Basic認証はcredentialsで指定してください。");
        }

        return uri;
    }

    private static Task ValidateHostAsync(Uri uri, CancellationToken ct) =>
        ValidateResolvedHostAsync(uri.Host, ct);

    /// <summary>ホスト名またはIPを解決し、遮断対象を含んでいれば拒否する。</summary>
    private static async Task ValidateResolvedHostAsync(string host, CancellationToken ct)
    {
        IPAddress[] addresses;

        if (IPAddress.TryParse(host, out var literal))
        {
            addresses = [literal];
        }
        else
        {
            if (host.Equals("localhost", StringComparison.OrdinalIgnoreCase))
            {
                throw Blocked();
            }

            try
            {
                addresses = await Dns.GetHostAddressesAsync(host, ct);
            }
            catch (SocketException)
            {
                throw AppException.BadRequest("host_unresolvable", "ホスト名を解決できません。");
            }

            if (addresses.Length == 0)
            {
                throw AppException.BadRequest("host_unresolvable", "ホスト名を解決できません。");
            }
        }

        if (addresses.Any(IsBlockedAddress))
        {
            throw Blocked();
        }
    }

    /// <summary>
    /// 接続時のDNS再解決対策。ホストを解決し、遮断対象を除いた接続許可IPだけを返す。
    /// SocketsHttpHandler.ConnectCallbackから呼び、検証済みIPへのみ接続する。
    /// </summary>
    internal static async Task<IPAddress[]> ResolveAllowedAddressesAsync(string host, CancellationToken ct)
    {
        IPAddress[] addresses;
        if (IPAddress.TryParse(host, out var literal))
        {
            addresses = [literal];
        }
        else
        {
            addresses = await Dns.GetHostAddressesAsync(host, ct);
        }

        var allowed = addresses.Where(a => !IsBlockedAddress(a)).ToArray();
        if (allowed.Length == 0)
        {
            throw new HttpRequestException(
                "Connection blocked: the host resolves only to disallowed addresses.");
        }

        return allowed;
    }

    internal static bool IsBlockedAddress(IPAddress address)
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
            return true;
        }

        var bytes = address.GetAddressBytes();

        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            // リンクローカル 169.254.0.0/16(クラウドメタデータ169.254.169.254を含む)
            if (bytes[0] == 169 && bytes[1] == 254)
            {
                return true;
            }

            // マルチキャスト 224.0.0.0/4
            if (bytes[0] >= 224 && bytes[0] <= 239)
            {
                return true;
            }
        }

        if (address.AddressFamily == AddressFamily.InterNetworkV6 &&
            (address.IsIPv6LinkLocal || address.IsIPv6Multicast))
        {
            return true;
        }

        return false;
    }

    private static AppException Blocked() => AppException.BadRequest(
        "url_not_allowed",
        "localhost・リンクローカル・メタデータIP・マルチキャスト宛の接続先は登録できません。");
}
