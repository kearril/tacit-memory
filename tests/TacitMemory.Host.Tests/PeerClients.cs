using System.Net;
using System.Net.Sockets;

namespace TacitMemory.Host.Tests;

/// <summary>
/// The two clients of the connection tests. The host counts as local only the connections whose peer is <c>127.0.0.1</c>
/// (see <see cref="OnlyLoopbackIsLocal"/>), so the remote client binds its end to <c>127.0.0.2</c>, another loopback
/// address that the host does not count as local. Windows accepts the whole <c>127.0.0.0/8</c> as local addresses.
/// </summary>
internal static class PeerClients
{
    /// <summary>The address the remote client connects from, and the extra listener of the tests that use one.</summary>
    public static readonly IPAddress RemoteAddress = IPAddress.Parse("127.0.0.2");

    /// <summary>The narrowed locality: a connection is local only when its peer is <c>127.0.0.1</c>.</summary>
    public static bool OnlyLoopbackIsLocal(IPAddress? peer) => peer?.Equals(IPAddress.Loopback) ?? false;

    /// <summary>A client from <c>127.0.0.1</c>: a local connection.</summary>
    public static HttpClient Local() => new();

    /// <summary>A client from <c>127.0.0.2</c>: a connection the narrowed host treats as remote.</summary>
    /// <summary>
    /// A client from <c>127.0.0.2</c>. It keeps no cookies: a test sends exactly the cookie it names, so a session that is
    /// refused or still valid is the one the test made, not one the client stored from an earlier reply.
    /// </summary>
    public static HttpClient Remote() => new(new SocketsHttpHandler { UseCookies = false, ConnectCallback = ConnectFromRemoteAddress });

    private static async ValueTask<Stream> ConnectFromRemoteAddress(SocketsHttpConnectionContext context, CancellationToken cancellationToken)
    {
        var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
        try
        {
            socket.Bind(new IPEndPoint(RemoteAddress, 0));
            await socket.ConnectAsync(context.DnsEndPoint, cancellationToken);
        }
        catch
        {
            socket.Dispose();
            throw;
        }
        return new NetworkStream(socket, ownsSocket: true);
    }
}
