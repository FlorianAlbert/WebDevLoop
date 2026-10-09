using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Ports;

namespace WebDevLoop.Core.Tests.Ports.Fakes;

public sealed class FakeTestTargetRunner : ITestTargetRunner
{
    private readonly HashSet<int> _reserved = [];

    public TestTargetReadiness Readiness { get; set; } = TestTargetReadiness.Ready;

    public int LeftoverProcesses { get; set; }

    public Task<TestTarget?> ReserveAsync(RunId specRunId, TestPortRange portRange, CancellationToken cancellationToken)
    {
        int[] free = Enumerable.Range(portRange.Start, portRange.End - portRange.Start + 1).Where(port => !_reserved.Contains(port)).Take(1).ToArray();
        if (free.Length == 0)
        {
            return Task.FromResult<TestTarget?>(null);
        }

        int port = free[0];

        _reserved.Add(port);
        var appUrl = new Uri($"http://127.0.0.1:{port}");
        return Task.FromResult<TestTarget?>(new TestTarget(specRunId, port, appUrl, new Dictionary<string, string> { ["PORT"] = port.ToString() }));
    }

    public Task<TestTargetReadiness> WaitForReadyAsync(TestTarget target, TimeSpan timeout, CancellationToken cancellationToken) =>
        Task.FromResult(Readiness);

    public Task<TestTargetStopResult> StopAsync(TestTarget target, CancellationToken cancellationToken)
    {
        _reserved.Remove(target.Port);
        int killed = LeftoverProcesses;
        LeftoverProcesses = 0;
        return Task.FromResult(new TestTargetStopResult(killed));
    }
}
