using WebDevLoop.Core.Agents;
using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Orchestration.Recovery.AgentSteps;
using WebDevLoop.Core.Orchestration.TicketExecution;
using WebDevLoop.Core.Tests.Orchestration.TicketExecution;

namespace WebDevLoop.Core.Tests.Orchestration.Recovery.AgentSteps;

public sealed class ImplementerSessionRecoveryTests
{
    private static readonly CopilotAuthIdentity User = new("octocat");

    private readonly AgentStepRecoveryFixture _fixture = new();

    [Fact]
    public async Task running_implementer_step_with_resumable_session_calls_resume()
    {
        (SeededSpec spec, StepRun crashed) = await CrashWhileImplementingAsync();
        CommitSha partial = _fixture.Execution.Git.CommitInWorktree(crashed.WorktreePath!, "partial.cs");
        _fixture.Restart();

        AgentStepRecoveryReport report = await _fixture.RecoverAsync();

        var session = new AgentSessionId(crashed.CopilotSessionId!);
        StepRun interrupted = _fixture.Step(crashed.Id.Value);
        Assert.Equal((StepStatus.Failed, StepInterruption.Restarted), (interrupted.Status, interrupted.FailureReason));
        Assert.Equal([crashed.Id], report.InterruptedSteps);
        Assert.Equal([new RecoveredWork(RecoveredWorkKind.Implementation, spec.Id, spec[1], session)], report.Relaunched);
        ImplementationAssignment assignment = _fixture.Execution.Launcher.Launched[^1];
        Assert.Equal(new ImplementationAssignment(spec.Id, spec[1], session), assignment);

        ImplementationResult result = await _fixture.Execution.Runner().RunAsync(assignment, AgentStepRecoveryFixture.Token);

        Assert.Equal(ImplementationResult.Implemented, result);
        AgentRunRequest resumed = Assert.Single(_fixture.Execution.Agents.Resumed);
        Assert.Equal((session, crashed.WorktreePath), (resumed.SessionId, resumed.Policy.Paths.WorkingDirectory));
        Assert.Equal(AgentRole.Implementer, resumed.Role);
        TicketRun ticket = _fixture.Ticket(spec[1]);
        Assert.Equal(TicketRunStatus.Reviewing, ticket.Status);
        Assert.True(await _fixture.Execution.Git.IsAncestorAsync(spec.Location, partial, ticket.LastImplementedSha!.Value, AgentStepRecoveryFixture.Token));
    }

    [Fact]
    public async Task missing_session_restarts_implementer_on_same_assigned_branch()
    {
        (SeededSpec spec, StepRun crashed) = await CrashWhileImplementingAsync();
        var session = new AgentSessionId(crashed.CopilotSessionId!);
        _fixture.Execution.Agents.MissingSessions.Add(session);
        _fixture.Restart();

        await _fixture.RecoverAsync();
        ImplementationResult result = await _fixture.Execution.Runner().RunAsync(_fixture.Execution.Launcher.Launched[^1], AgentStepRecoveryFixture.Token);

        Assert.Equal(ImplementationResult.Implemented, result);
        Assert.Equal(session, Assert.Single(_fixture.Execution.Agents.Resumed).SessionId);
        AgentRunRequest restarted = _fixture.Execution.Agents.Started[^1];
        Assert.NotEqual(session, restarted.SessionId);
        Assert.Equal(crashed.WorktreePath, restarted.Policy.Paths.WorkingDirectory);
        TicketRun ticket = _fixture.Ticket(spec[1]);
        StepRun step = _fixture.Execution.Steps(spec[1])[^1];
        Assert.Equal((StepStatus.Succeeded, restarted.SessionId.Value, ticket.BranchName), (step.Status, step.CopilotSessionId, step.BranchName));
        Assert.Equal((TicketRunStatus.Reviewing, crashed.BranchName), (ticket.Status, (BranchName?)ticket.BranchName));
    }

    [Fact]
    public async Task token_expiry_during_recovery_replaces_runtime_before_resume()
    {
        // Another live session keeps the runtime busy, so it is refreshed rather than evicted as idle.
        CopilotRuntimeLease busy = await _fixture.Runtimes.AcquireAsync(User, AgentStepRecoveryFixture.Token);
        (_, StepRun crashed) = await CrashWhileImplementingAsync();
        _fixture.Clock.Advance(TimeSpan.FromMinutes(56));
        _fixture.Restart();

        AgentStepRecoveryReport report = await _fixture.RecoverAsync();
        await _fixture.Execution.Runner().RunAsync(_fixture.Execution.Launcher.Launched[^1], AgentStepRecoveryFixture.Token);
        await busy.DisposeAsync();

        CopilotRuntimeKey stale = Assert.Single(report.RefreshedRuntimes);
        Assert.Equal((User, 1), (stale.Identity, stale.TokenGeneration));
        Assert.Null(report.RuntimeMaintenanceFailure);
        Assert.Equal(["refresh 1", $"agent {crashed.CopilotSessionId}"], _fixture.Journal);
        await using CopilotRuntimeLease current = await _fixture.Runtimes.AcquireAsync(User, AgentStepRecoveryFixture.Token);
        Assert.Equal(2, current.Key.TokenGeneration);
    }

    [Fact]
    public async Task steps_started_by_this_process_are_left_to_their_runner()
    {
        (_, StepRun running) = await CrashWhileImplementingAsync();

        AgentStepRecoveryReport report = await _fixture.RecoverAsync();

        Assert.Equal(StepStatus.Running, _fixture.Step(running.Id.Value).Status);
        Assert.Empty(report.InterruptedSteps);
        Assert.Empty(report.Relaunched);
    }

    /// <summary>The implementer of ticket #1 is in flight when the process dies: its step stays running.</summary>
    private async Task<(SeededSpec Spec, StepRun Crashed)> CrashWhileImplementingAsync()
    {
        SeededSpec spec = await _fixture.Execution.SeedRunningSpecAsync("app", (1, []));
        _fixture.Execution.UseRunner();
        await _fixture.Execution.ReconcileAsync(spec.Id);
        _fixture.Execution.Launcher.Run = null;
        StepRun crashed = Assert.Single(_fixture.Execution.Steps(spec[1]));
        Assert.Equal(StepStatus.Running, crashed.Status);
        _fixture.Execution.Agents.AutoReply = request =>
        {
            _fixture.Journal.Add($"agent {request.SessionId}");
            return TicketExecutionFixture.Completed(_fixture.Execution.CommitInWorktree(request, "rest.cs"));
        };
        return (spec, crashed);
    }
}
