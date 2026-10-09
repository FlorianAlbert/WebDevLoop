namespace WebDevLoop.Infrastructure.TestHost;

public sealed record TestHostOptions
{
    /// <summary>How often the reserved port is probed while waiting for the application the tester starts.</summary>
    public TimeSpan ReadinessPollInterval { get; init; } = TimeSpan.FromMilliseconds(500);

    /// <summary>How long one readiness probe waits for a TCP connection.</summary>
    public TimeSpan ConnectTimeout { get; init; } = TimeSpan.FromSeconds(1);
}
