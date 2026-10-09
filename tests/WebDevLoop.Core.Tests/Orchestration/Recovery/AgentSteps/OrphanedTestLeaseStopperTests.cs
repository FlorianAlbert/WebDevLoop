using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Orchestration.Recovery.AgentSteps;
using WebDevLoop.Core.Tests.Orchestration.TicketExecution;

namespace WebDevLoop.Core.Tests.Orchestration.Recovery.AgentSteps;

/// <summary>The single stopper of orphaned tester processes (formerly also covered by the startup-only lease reaper).</summary>
public sealed class OrphanedTestLeaseStopperTests
{
    private readonly AgentStepRecoveryFixture _fixture = new();

    [Fact]
    public async Task restart_kills_leftover_processes_of_every_orphaned_lease_and_releases_it()
    {
        SeededSpec first = await _fixture.Testing.SeedTestingAsync();
        SeededSpec second = await _fixture.Testing.Parent.SeedParentReviewingAsync();
        DateTimeOffset at = _fixture.Clock.UtcNow;
        _fixture.Testing.Leases.Seed(TestLease.Acquire(first.Id, 41003, "/work/runs/a/test", at, TimeSpan.FromHours(1)));
        _fixture.Testing.Leases.Seed(TestLease.Acquire(second.Id, 41004, "/work/runs/b/test", at - TimeSpan.FromHours(2), TimeSpan.FromHours(1)));
        TestLease released = TestLease.Acquire(second.Id, 41005, "/work/runs/b/test", at, TimeSpan.FromHours(1));
        released.Release(at);
        _fixture.Testing.Leases.Seed(released);
        _fixture.Testing.Target.LeftoverProcesses = 2;
        _fixture.Restart();

        IReadOnlyList<StoppedTestLease> stopped = await _fixture.LeaseStopper().StopAsync(AgentStepRecoveryFixture.Token);

        Assert.Equal(
            [(first.Id, 41003, false), (second.Id, 41004, true)],
            stopped.Select(lease => (lease.SpecRunId, lease.Port, lease.Expired)).OrderBy(lease => lease.Port));
        Assert.Equal(2, stopped.Sum(lease => lease.KilledProcesses));
        Assert.Equal(
            [(first.Id, 41003), (second.Id, 41004)],
            _fixture.Testing.Target.Stopped.Select(stop => (stop.Target.SpecRunId, stop.Target.Port)).OrderBy(stop => stop.Port));
        Assert.All(_fixture.Testing.Leases.Rows, lease => Assert.False(lease.IsActive));
    }

    [Fact]
    public async Task without_active_leases_nothing_is_stopped()
    {
        _fixture.Restart();

        IReadOnlyList<StoppedTestLease> stopped = await _fixture.LeaseStopper().StopAsync(AgentStepRecoveryFixture.Token);

        Assert.Empty(stopped);
        Assert.Empty(_fixture.Testing.Target.Stopped);
    }
}
