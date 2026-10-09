using System.Diagnostics;
using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Ports;

namespace WebDevLoop.Infrastructure.TestHost;

/// <summary>
/// Supervises the application a tester agent starts: reserves a port that is free on this machine and not reserved by
/// another tester run, hands the tester the port, URL and a lease marker through its shell environment
/// (<see cref="TestTargetEnvironment"/>), polls the port for readiness, and on stop kills every leftover process tagged
/// with the lease marker (see <see cref="LeftoverProcesses"/>). Stopping needs only the spec run and port, so recovery can
/// stop targets reserved by a previous app process.
/// </summary>
internal sealed class TestTargetRunner(IPortProbe ports, IProcessTable processes, IProcessTerminator terminator, TestHostOptions options) : ITestTargetRunner
{
    private readonly Lock _reservationLock = new();
    private readonly Dictionary<int, RunId> _reserved = [];

    public Task<TestTarget?> ReserveAsync(RunId specRunId, TestPortRange portRange, CancellationToken cancellationToken)
    {
        lock (_reservationLock)
        {
            for (int port = portRange.Start; port <= portRange.End; port++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!_reserved.ContainsKey(port) && ports.IsFree(port))
                {
                    _reserved[port] = specRunId;
                    return Task.FromResult<TestTarget?>(
                        new TestTarget(specRunId, port, TestTargetEnvironment.AppUrl(port), TestTargetEnvironment.For(specRunId, port)));
                }
            }
        }

        return Task.FromResult<TestTarget?>(null);
    }

    public async Task<TestTargetReadiness> WaitForReadyAsync(TestTarget target, TimeSpan timeout, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);
        var elapsed = Stopwatch.StartNew();
        while (true)
        {
            if (await ports.AcceptsConnectionsAsync(target.Port, cancellationToken))
            {
                return TestTargetReadiness.Ready;
            }

            TimeSpan remaining = timeout - elapsed.Elapsed;
            if (remaining <= TimeSpan.Zero)
            {
                return TestTargetReadiness.TimedOut;
            }

            await Task.Delay(remaining < options.ReadinessPollInterval ? remaining : options.ReadinessPollInterval, cancellationToken);
        }
    }

    public Task<TestTargetStopResult> StopAsync(TestTarget target, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);
        IReadOnlyList<int> leftovers = LeftoverProcesses.Find(
            processes.Snapshot(), processes.CurrentProcessId, TestTargetEnvironment.LeaseMarker(target.SpecRunId, target.Port));
        int killed = leftovers.Count(terminator.Kill);
        lock (_reservationLock)
        {
            if (_reserved.TryGetValue(target.Port, out RunId owner) && owner == target.SpecRunId)
            {
                _reserved.Remove(target.Port);
            }
        }

        return Task.FromResult(new TestTargetStopResult(killed));
    }
}
