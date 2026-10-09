using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Orchestration.Completion.Testing;
using WebDevLoop.Core.Tests.Orchestration.TicketExecution;

namespace WebDevLoop.Core.Tests.Orchestration.Completion.Testing;

public sealed class TestLeaseReaperTests
{
    private readonly TestingFixture _fixture = new();

    [Fact]
    public async Task Restart_kills_leftover_processes_of_every_orphaned_lease_and_releases_it()
    {
        SeededSpec first = await _fixture.SeedTestingAsync();
        SeededSpec second = await _fixture.Parent.SeedParentReviewingAsync();
        DateTimeOffset at = _fixture.Execution.Clock.UtcNow;
        _fixture.Leases.Seed(TestLease.Acquire(first.Id, 41003, "/work/runs/a/test", at, TimeSpan.FromHours(1)));
        _fixture.Leases.Seed(TestLease.Acquire(second.Id, 41004, "/work/runs/b/test", at - TimeSpan.FromHours(2), TimeSpan.FromHours(1)));
        TestLease released = TestLease.Acquire(second.Id, 41005, "/work/runs/b/test", at, TimeSpan.FromHours(1));
        released.Release(at);
        _fixture.Leases.Seed(released);
        _fixture.Target.LeftoverProcesses = 2;

        int stopped = await _fixture.Reaper().StopOrphanedAsync(TestingFixture.Token);

        Assert.Equal(2, stopped);
        Assert.Equal([(first.Id, 41003), (second.Id, 41004)], _fixture.Target.Stopped.Select(stop => (stop.Target.SpecRunId, stop.Target.Port)));
        Assert.Equal(2, _fixture.Target.Stopped[0].Killed);
        Assert.All(_fixture.Leases.Rows, lease => Assert.False(lease.IsActive));
    }

    [Fact]
    public async Task Without_active_leases_nothing_is_stopped()
    {
        int stopped = await _fixture.Reaper().StopOrphanedAsync(TestingFixture.Token);

        Assert.Equal(0, stopped);
        Assert.Empty(_fixture.Target.Stopped);
    }
}
