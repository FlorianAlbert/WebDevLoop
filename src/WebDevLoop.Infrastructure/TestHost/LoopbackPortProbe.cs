using System.Net;
using System.Net.Sockets;

namespace WebDevLoop.Infrastructure.TestHost;

/// <summary>Probes ports on this machine with TCP sockets, for IPv4 and (where supported) IPv6.</summary>
internal sealed class LoopbackPortProbe(TestHostOptions options) : IPortProbe
{
    public bool IsFree(int port) =>
        CanBind(IPAddress.Any, port) && (!Socket.OSSupportsIPv6 || CanBind(IPAddress.IPv6Any, port));

    public async Task<bool> AcceptsConnectionsAsync(int port, CancellationToken cancellationToken) =>
        await CanConnectAsync(IPAddress.Loopback, port, cancellationToken)
        || (Socket.OSSupportsIPv6 && await CanConnectAsync(IPAddress.IPv6Loopback, port, cancellationToken));

    private static bool CanBind(IPAddress address, int port)
    {
        try
        {
            using var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
            if (address.AddressFamily == AddressFamily.InterNetworkV6)
            {
                socket.DualMode = false;
            }

            socket.Bind(new IPEndPoint(address, port));
            return true;
        }
        catch (SocketException)
        {
            return false;
        }
    }

    private async Task<bool> CanConnectAsync(IPAddress address, int port, CancellationToken cancellationToken)
    {
        using var attempt = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        attempt.CancelAfter(options.ConnectTimeout);
        try
        {
            using var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
            await socket.ConnectAsync(new IPEndPoint(address, port), attempt.Token);
            return true;
        }
        catch (SocketException)
        {
            return false;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return false;
        }
    }
}
