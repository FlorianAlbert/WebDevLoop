using System.Net;
using System.Net.Sockets;
using WebDevLoop.Infrastructure.TestHost;

namespace WebDevLoop.Infrastructure.Tests.TestHost;

public sealed class LoopbackPortProbeTests
{
    private readonly LoopbackPortProbe _probe = new(new TestHostOptions { ConnectTimeout = TimeSpan.FromMilliseconds(500) });

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_port_with_a_listener_is_taken_and_accepts_connections()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;

        Assert.False(_probe.IsFree(port));
        Assert.True(await _probe.AcceptsConnectionsAsync(port, Token));
    }

    [Fact]
    public async Task A_released_port_is_free_and_accepts_nothing()
    {
        int port;
        using (var listener = new TcpListener(IPAddress.Loopback, 0))
        {
            listener.Start();
            port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
        }

        Assert.True(_probe.IsFree(port));
        Assert.False(await _probe.AcceptsConnectionsAsync(port, Token));
    }
}
