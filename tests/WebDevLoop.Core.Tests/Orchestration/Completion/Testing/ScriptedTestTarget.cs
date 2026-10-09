using System.Globalization;
using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Ports;

namespace WebDevLoop.Core.Tests.Orchestration.Completion.Testing;

/// <summary>How the fake application start behaves for one reservation.</summary>
internal enum FakeStartup
{
    Ready,
    TimedOut,

    /// <summary>Supervising the start throws (e.g. the started process crashed).</summary>
    Crashes,

    /// <summary>Never becomes ready; waits until the readiness wait is cancelled.</summary>
    Pending,
}

/// <summary>
/// Test-target supervisor fake: hands out ports from the configured range, plays a scripted application start per
/// reservation, and "kills" a configurable number of leftover processes on stop.
/// </summary>
internal sealed class ScriptedTestTarget : ITestTargetRunner
{
    public const string PortVariable = "PORT";
    public const string AppUrlVariable = "WEBDEVLOOP_APP_URL";

    private readonly HashSet<int> _reserved = [];
    private readonly Queue<FakeStartup> _startups = [];

    public bool NoFreePort { get; set; }

    /// <summary>Extra variables put into every target environment (e.g. to try injecting credentials).</summary>
    public Dictionary<string, string> ExtraEnvironment { get; } = new(StringComparer.Ordinal);

    /// <summary>Leftover processes found by the next stop.</summary>
    public int LeftoverProcesses { get; set; }

    public List<TestTarget> Reserved { get; } = [];

    public List<(TestTarget Target, int Killed)> Stopped { get; } = [];

    public List<TimeSpan> ReadinessTimeouts { get; } = [];

    public ScriptedTestTarget Startup(params FakeStartup[] startups)
    {
        foreach (FakeStartup startup in startups)
        {
            _startups.Enqueue(startup);
        }

        return this;
    }

    public Task<TestTarget?> ReserveAsync(RunId specRunId, TestPortRange portRange, CancellationToken cancellationToken)
    {
        int? port = NoFreePort
            ? null
            : Enumerable.Range(portRange.Start, portRange.End - portRange.Start + 1).Cast<int?>().FirstOrDefault(candidate => !_reserved.Contains(candidate!.Value));
        if (port is not { } free)
        {
            return Task.FromResult<TestTarget?>(null);
        }

        _reserved.Add(free);
        string number = free.ToString(CultureInfo.InvariantCulture);
        var url = new Uri($"http://localhost:{number}/");
        var environment = new Dictionary<string, string>(ExtraEnvironment, StringComparer.Ordinal)
        {
            [PortVariable] = number,
            [AppUrlVariable] = url.ToString(),
        };
        var target = new TestTarget(specRunId, free, url, environment);
        Reserved.Add(target);
        return Task.FromResult<TestTarget?>(target);
    }

    public async Task<TestTargetReadiness> WaitForReadyAsync(TestTarget target, TimeSpan timeout, CancellationToken cancellationToken)
    {
        ReadinessTimeouts.Add(timeout);
        switch (_startups.Count == 0 ? FakeStartup.Ready : _startups.Dequeue())
        {
            case FakeStartup.TimedOut:
                return TestTargetReadiness.TimedOut;
            case FakeStartup.Crashes:
                throw new InvalidOperationException("The application process exited with code 134.");
            case FakeStartup.Pending:
                await Task.Delay(Timeout.Infinite, cancellationToken);
                return TestTargetReadiness.TimedOut;
            default:
                return TestTargetReadiness.Ready;
        }
    }

    public Task<TestTargetStopResult> StopAsync(TestTarget target, CancellationToken cancellationToken)
    {
        _reserved.Remove(target.Port);
        int killed = LeftoverProcesses;
        LeftoverProcesses = 0;
        Stopped.Add((target, killed));
        return Task.FromResult(new TestTargetStopResult(killed));
    }
}
