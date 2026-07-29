using System.Net.Sockets;

namespace ServerOperations.Core.Adapters.Implementations;

/// <summary>
/// アダプター用HTTPハンドラー。リダイレクトを追跡せず、接続時に自前でDNS解決して
/// 遮断対象(ループバック・リンクローカル等)を除いた検証済みIPだけへ接続する
/// (登録時検証後にDNSの解決先が差し替えられるDNS rebindingへの対策)。
/// </summary>
public static class AdapterHttpHandlerFactory
{
    public static SocketsHttpHandler CreateGuardedHandler() => new()
    {
        AllowAutoRedirect = false,
        ConnectCallback = async (context, ct) =>
        {
            var allowed = await EndpointValidator.ResolveAllowedAddressesAsync(context.DnsEndPoint.Host, ct);

            var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
            try
            {
                await socket.ConnectAsync(allowed, context.DnsEndPoint.Port, ct);
                return new NetworkStream(socket, ownsSocket: true);
            }
            catch
            {
                socket.Dispose();
                throw;
            }
        },
    };
}
