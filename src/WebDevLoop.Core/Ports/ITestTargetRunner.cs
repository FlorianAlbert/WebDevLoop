using WebDevLoop.Core.Domain;

namespace WebDevLoop.Core.Ports;

/// <summary>
/// Supervises the app the tester agent starts from the configured run instructions: reserves an isolated port,
/// observes readiness, and kills leftover process groups on completion, timeout, abort, or restart.
/// </summary>
public interface ITestTargetRunner
{
    /// <summary>Returns null when no port in <paramref name="portRange"/> is free.</summary>
    Task<TestTarget?> ReserveAsync(RunId specRunId, TestPortRange portRange, CancellationToken cancellationToken);

    Task<TestTargetReadiness> WaitForReadyAsync(TestTarget target, TimeSpan timeout, CancellationToken cancellationToken);

    /// <summary>Idempotent; also used by recovery for expired leases.</summary>
    Task<TestTargetStopResult> StopAsync(TestTarget target, CancellationToken cancellationToken);
}
