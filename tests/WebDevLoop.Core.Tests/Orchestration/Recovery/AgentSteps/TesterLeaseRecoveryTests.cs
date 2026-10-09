using WebDevLoop.Core.Agents;
using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Orchestration.Completion.Testing;
using WebDevLoop.Core.Orchestration.Recovery.AgentSteps;
using WebDevLoop.Core.Tests.Orchestration.TicketExecution;

namespace WebDevLoop.Core.Tests.Orchestration.Recovery.AgentSteps;

public sealed class TesterLeaseRecoveryTests
{
    private const int Port = 41003;

    private readonly AgentStepRecoveryFixture _fixture = new();

    [Fact]
    public async Task expired_tester_lease_is_killed_and_test_step_marked_timed_out()
    {
        SeededSpec spec = await _fixture.Testing.SeedTestingAsync();
        StepRun step = await _fixture.SeedRunningStepAsync(spec.Id, null, StepKind.Test, AgentRole.Tester, "test-1");
        _fixture.SeedLease(spec.Id, Port, TimeSpan.FromMinutes(40));
        _fixture.Testing.Target.LeftoverProcesses = 2;
        _fixture.Clock.Advance(TimeSpan.FromHours(1));

        AgentStepRecoveryReport report = await _fixture.RecoverAsync();

        Assert.Equal([(spec.Id, Port, 2)], _fixture.Testing.Target.Stopped.Select(stop => (stop.Target.SpecRunId, stop.Target.Port, stop.Killed)));
        Assert.All(_fixture.Testing.Leases.Rows, lease => Assert.False(lease.IsActive));
        Assert.Equal([new StoppedTestLease(spec.Id, Port, Expired: true, KilledProcesses: 2)], report.StoppedTestLeases);
        StepRun timedOut = _fixture.Step("test-1");
        string reason = StepInterruption.LeaseExpired(Port, 2);
        Assert.Equal((StepStatus.TimedOut, reason), (timedOut.Status, timedOut.FailureReason));
        AgentLogEntry log = Assert.Single(_fixture.Logs.Entries);
        Assert.Equal((step.Id, AgentLogKind.Error, reason), (log.StepRunId, log.Kind, log.Text));
        Assert.Equal([new TestingAssignment(spec.Id)], _fixture.Testing.Launcher.Launched);
    }

    [Fact]
    public async Task lease_left_by_a_previous_process_is_killed_and_its_test_restarted()
    {
        SeededSpec spec = await _fixture.Testing.SeedTestingAsync();
        await _fixture.SeedRunningStepAsync(spec.Id, null, StepKind.Test, AgentRole.Tester, "test-1");
        _fixture.SeedLease(spec.Id, Port, TimeSpan.FromHours(1));
        _fixture.Restart();

        AgentStepRecoveryReport report = await _fixture.RecoverAsync();

        Assert.Equal([new StoppedTestLease(spec.Id, Port, Expired: false, KilledProcesses: 0)], report.StoppedTestLeases);
        StepRun interrupted = _fixture.Step("test-1");
        Assert.Equal((StepStatus.Failed, StepInterruption.LeaseReleased(Port, 0)), (interrupted.Status, interrupted.FailureReason));
        Assert.Equal([new TestingAssignment(spec.Id)], _fixture.Testing.Launcher.Launched);
    }

    [Fact]
    public async Task an_unexpired_lease_of_this_process_belongs_to_its_live_tester()
    {
        SeededSpec spec = await _fixture.Testing.SeedTestingAsync();
        await _fixture.SeedRunningStepAsync(spec.Id, null, StepKind.Test, AgentRole.Tester, "test-1");
        _fixture.SeedLease(spec.Id, Port, TimeSpan.FromHours(1));
        _fixture.Clock.Advance(TimeSpan.FromMinutes(30));

        AgentStepRecoveryReport report = await _fixture.RecoverAsync();

        Assert.Empty(report.StoppedTestLeases);
        Assert.Empty(_fixture.Testing.Target.Stopped);
        Assert.Equal(StepStatus.Running, _fixture.Step("test-1").Status);
        Assert.Empty(_fixture.Testing.Launcher.Launched);
    }
}
