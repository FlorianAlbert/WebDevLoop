namespace WebDevLoop.Infrastructure.TestHost;

internal interface IPortProbe
{
    /// <summary>True when nothing listens on <paramref name="port"/>, so the tester's application can bind it.</summary>
    bool IsFree(int port);

    /// <summary>True when something on this machine accepts TCP connections on <paramref name="port"/>.</summary>
    Task<bool> AcceptsConnectionsAsync(int port, CancellationToken cancellationToken);
}
