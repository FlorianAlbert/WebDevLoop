using WebDevLoop.Core.Agents;
using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Events;
using WebDevLoop.Core.Orchestration.Results;
using WebDevLoop.Core.Orchestration.TicketExecution;
using WebDevLoop.Core.Ports;

namespace WebDevLoop.Core.Tests.Orchestration.TicketExecution;

public sealed class TicketImplementationRunnerTests
{
    private readonly TicketExecutionFixture _fixture = new();

    public TicketImplementationRunnerTests() => _fixture.UseRunner();

    private static CancellationToken Token => TicketExecutionFixture.Token;

    [Fact]
    public async Task implementer_starts_in_a_fresh_worktree_on_the_integration_tip()
    {
        _fixture.UseImplementerTemplate("Implement {ticket_title} in {worktree_path} on {branch_name} from {integration_tip_sha}.");
        SeededSpec spec = await _fixture.SeedRunningSpecAsync("app", (1, []));
        CommitSha tip = _fixture.Spec(spec.Id).IntegrationTipSha!.Value;

        await _fixture.ReconcileAsync(spec.Id);

        TicketRun ticket = _fixture.Ticket(spec[1]);
        string worktree = $"/work/runs/{spec.Id}/tickets/{ticket.Id}";
        AgentRunRequest request = Assert.Single(_fixture.Agents.Started);
        Assert.Equal(AgentRole.Implementer, request.Role);
        Assert.Equal(worktree, request.Policy.Paths.WorkingDirectory);
        Assert.Equal($"Implement Ticket 1 in {worktree} on {ticket.BranchName} from {tip}.", request.Prompt);
        Assert.Equal(new WorktreeInspection(WorktreeStatus.Clean, ticket.BranchName, tip), await _fixture.Git.InspectWorktreeAsync(spec.Location, worktree, Token));
        StepRun step = Assert.Single(_fixture.Steps(ticket.Id));
        Assert.Equal((StepKind.Implement, StepStatus.Running, 1), (step.Kind, step.Status, step.Attempt));
        Assert.Equal(request.SessionId.Value, step.CopilotSessionId);
        Assert.Equal(request.StepRunId, step.Id);
        Assert.Equal(worktree, step.WorktreePath);
        Assert.False(string.IsNullOrWhiteSpace(step.Model));
        Assert.Equal((request.Settings.Model, request.Settings.ReasoningEffort), (step.Model, step.ReasoningEffort));
    }

    [Fact]
    public async Task validated_report_moves_the_ticket_to_reviewing()
    {
        SeededSpec spec = await _fixture.SeedRunningSpecAsync("app", (1, []));
        await _fixture.ReconcileAsync(spec.Id);
        AgentRunRequest request = Assert.Single(_fixture.Agents.Started);
        CommitSha head = _fixture.CommitInWorktree(request);

        _fixture.Agents.Reply(request, TicketExecutionFixture.Completed(head));
        await _fixture.Launcher.WhenAllFinishedAsync();

        Assert.Equal([ImplementationResult.Implemented], _fixture.Results);
        TicketRun ticket = _fixture.Ticket(spec[1]);
        Assert.Equal(TicketRunStatus.Reviewing, ticket.Status);
        Assert.Equal(head, ticket.LastImplementedSha);
        StepRun step = Assert.Single(_fixture.Steps(ticket.Id));
        Assert.Equal(StepStatus.Succeeded, step.Status);
        Assert.Contains(head.Value, step.StructuredResultJson);
        Assert.Contains(_fixture.Db.CommittedEvents.OfType<TicketRunStatusChanged>(), changed => changed.To == TicketRunStatus.Reviewing);
    }

    [Fact]
    public async Task implementation_report_with_unexpected_sha_is_rejected()
    {
        SeededSpec spec = await _fixture.SeedRunningSpecAsync("app", (1, []));
        await _fixture.ReconcileAsync(spec.Id);
        AgentRunRequest request = Assert.Single(_fixture.Agents.Started);
        CommitSha branchHead = _fixture.CommitInWorktree(request);
        CommitSha elsewhere = _fixture.Git.Commit([branchHead], "unrelated.cs");

        _fixture.Agents.Reply(request, TicketExecutionFixture.Completed(elsewhere));
        await _fixture.Launcher.WhenAllFinishedAsync();

        ImplementationResult result = Assert.Single(_fixture.Results);
        Assert.Equal(ImplementationOutcome.UnexpectedCommitSha, result.Outcome);
        Assert.Contains(elsewhere.Value, result.Reason);
        Assert.Contains(branchHead.Value, result.Reason);
        TicketRun ticket = _fixture.Ticket(spec[1]);
        Assert.Equal(TicketRunStatus.NeedsAttention, ticket.Status);
        Assert.Equal(result.Reason, ticket.FailureReason);
        Assert.Null(ticket.LastImplementedSha);
        Assert.Equal(StepStatus.Failed, Assert.Single(_fixture.Steps(ticket.Id)).Status);
        Assert.DoesNotContain(_fixture.Db.CommittedEvents.OfType<TicketRunStatusChanged>(), changed => changed.To == TicketRunStatus.Reviewing);
    }

    [Fact]
    public async Task integration_tip_advancing_during_the_implementer_turn_does_not_fail_validation()
    {
        SeededSpec spec = await _fixture.SeedRunningSpecAsync("app", (1, []), (2, []));
        await _fixture.ReconcileAsync(spec.Id);
        AgentRunRequest first = _fixture.Agents.InFlightIn(_fixture.Ticket(spec[1]).WorktreePath!);
        AgentRunRequest second = _fixture.Agents.InFlightIn(_fixture.Ticket(spec[2]).WorktreePath!);
        _fixture.Agents.Reply(first, TicketExecutionFixture.Completed(_fixture.CommitInWorktree(first)));
        await _fixture.IntegrateAsync(spec, spec[1]);

        CommitSha basedOnStartTip = _fixture.CommitInWorktree(second, "other.cs");
        _fixture.Agents.Reply(second, TicketExecutionFixture.Completed(basedOnStartTip));
        await _fixture.Launcher.WhenAllFinishedAsync();

        Assert.Equal(ImplementationOutcome.Implemented, _fixture.Results[^1].Outcome);
        TicketRun ticket = _fixture.Ticket(spec[2]);
        Assert.Equal(TicketRunStatus.Reviewing, ticket.Status);
        Assert.Equal(basedOnStartTip, ticket.LastImplementedSha);
    }

    [Fact]
    public async Task ticket_branch_missing_the_integration_tip_the_implementer_started_from_returns_fix_signal()
    {
        SeededSpec spec = await _fixture.SeedRunningSpecAsync("app", (1, []), (2, [1]));
        CommitSha baseSha = _fixture.Spec(spec.Id).IntegrationBaseSha!.Value;
        await _fixture.ReconcileAsync(spec.Id);
        AgentRunRequest first = Assert.Single(_fixture.Agents.InFlight);
        _fixture.Agents.Reply(first, TicketExecutionFixture.Completed(_fixture.CommitInWorktree(first)));
        await _fixture.IntegrateAsync(spec, spec[1]);
        await _fixture.ReconcileAsync(spec.Id);
        AgentRunRequest request = Assert.Single(_fixture.Agents.InFlight);
        CommitSha startTip = _fixture.Spec(spec.Id).IntegrationTipSha!.Value;
        TicketRun ticket = _fixture.Ticket(spec[2]);
        await _fixture.Git.PrepareWorktreeAsync(spec.Location, new WorktreeSpec(ticket.BranchName, baseSha, ticket.WorktreePath!), Token);

        _fixture.Agents.Reply(request, TicketExecutionFixture.Completed(_fixture.CommitInWorktree(request, "reset.cs")));
        await _fixture.Launcher.WhenAllFinishedAsync();

        ImplementationResult result = _fixture.Results[^1];
        Assert.Equal(ImplementationOutcome.IntegrationMergeMissing, result.Outcome);
        Assert.Contains(startTip.Value, result.Reason);
        Assert.Equal(TicketRunStatus.NeedsAttention, _fixture.Ticket(spec[2]).Status);
        StepRun step = Assert.Single(_fixture.Steps(ticket.Id));
        Assert.Equal(StepStatus.NeedsAttention, step.Status);
        Assert.Equal(request.SessionId.Value, step.CopilotSessionId);
    }

    [Fact]
    public async Task implementer_that_merged_the_advanced_integration_tip_passes_validation()
    {
        SeededSpec spec = await _fixture.SeedRunningSpecAsync("app", (1, []), (2, []));
        await _fixture.ReconcileAsync(spec.Id);
        AgentRunRequest first = _fixture.Agents.InFlightIn(_fixture.Ticket(spec[1]).WorktreePath!);
        AgentRunRequest second = _fixture.Agents.InFlightIn(_fixture.Ticket(spec[2]).WorktreePath!);
        _fixture.Agents.Reply(first, TicketExecutionFixture.Completed(_fixture.CommitInWorktree(first)));
        await _fixture.IntegrateAsync(spec, spec[1]);

        _fixture.CommitInWorktree(second, "other.cs");
        CommitSha merged = await _fixture.MergeIntegrationTipAsync(second, spec);
        _fixture.Agents.Reply(second, TicketExecutionFixture.Completed(merged));
        await _fixture.Launcher.WhenAllFinishedAsync();

        Assert.Equal(ImplementationOutcome.Implemented, _fixture.Results[^1].Outcome);
        Assert.Equal(TicketRunStatus.Reviewing, _fixture.Ticket(spec[2]).Status);
        Assert.Equal(merged, _fixture.Ticket(spec[2]).LastImplementedSha);
    }

    [Fact]
    public async Task duplicate_launches_start_exactly_one_implementer_session()
    {
        SeededSpec spec = await _fixture.SeedRunningSpecAsync("app", (1, []));
        await _fixture.ReconcileAsync(spec.Id);

        _fixture.Launcher.Launch(new ImplementationAssignment(spec.Id, spec[1]));

        Assert.Single(_fixture.Agents.Started);
        Assert.Equal([ImplementationResult.AlreadyRunning], _fixture.Results);
        Assert.Single(_fixture.Steps(spec[1]));
    }

    [Fact]
    public async Task racing_duplicate_runners_start_exactly_one_implementer_session()
    {
        SeededSpec spec = await _fixture.SeedRunningSpecAsync("app", (1, []));
        await _fixture.MoveAsync(spec[1], TicketRunStatus.Ready, TicketRunStatus.Implementing);
        var assignment = new ImplementationAssignment(spec.Id, spec[1]);
        _fixture.Db.HoldSavesUntil(parties: 2);

        Task<ImplementationResult> first = _fixture.Runner().RunAsync(assignment, Token);
        Task<ImplementationResult> second = _fixture.Runner().RunAsync(assignment, Token);

        Assert.Single(_fixture.Agents.Started);
        Assert.Equal(ImplementationResult.ConcurrencyConflict, await second);
        Assert.False(first.IsCompleted);
        Assert.Single(_fixture.Steps(spec[1]));
    }

    [Fact]
    public async Task failed_agent_turns_are_retried_in_a_fresh_session_then_need_attention()
    {
        _fixture.Settings.Defaults = _fixture.Settings.Defaults with { MaxRetries = 1 };
        _fixture.Agents.AutoReply = _ => AgentRunResult.NotReported(AgentRunOutcome.MissingReport, "No report.");
        SeededSpec spec = await _fixture.SeedRunningSpecAsync("app", (1, []));

        await _fixture.ReconcileAsync(spec.Id);
        await _fixture.Launcher.WhenAllFinishedAsync();

        ImplementationResult result = Assert.Single(_fixture.Results);
        Assert.Equal(ImplementationOutcome.Failed, result.Outcome);
        Assert.Contains("No report.", result.Reason);
        Assert.Equal(2, _fixture.Agents.Started.Select(request => request.SessionId).Distinct().Count());
        Assert.Equal([(1, StepStatus.Failed), (2, StepStatus.Failed)], _fixture.Steps(spec[1]).Select(step => (step.Attempt, step.Status)));
        Assert.Equal(TicketRunStatus.NeedsAttention, _fixture.Ticket(spec[1]).Status);
    }

    [Fact]
    public async Task a_blocked_first_attempt_is_retried_and_can_still_succeed()
    {
        SeededSpec spec = await _fixture.SeedRunningSpecAsync("app", (1, []));
        await _fixture.ReconcileAsync(spec.Id);
        AgentRunRequest blocked = Assert.Single(_fixture.Agents.Started);
        _fixture.CommitInWorktree(blocked, "half-done.cs");

        _fixture.Agents.Reply(blocked, AgentRunResult.Reported(ImplementationReport.Blocked("Missing fixture data.")));
        AgentRunRequest retry = _fixture.Agents.InFlight.Single();
        CommitSha tip = _fixture.Spec(spec.Id).IntegrationTipSha!.Value;
        WorktreeInspection reset = await _fixture.Git.InspectWorktreeAsync(spec.Location, retry.Policy.Paths.WorkingDirectory, Token);
        _fixture.Agents.Reply(retry, TicketExecutionFixture.Completed(_fixture.CommitInWorktree(retry)));
        await _fixture.Launcher.WhenAllFinishedAsync();

        Assert.NotEqual(blocked.SessionId, retry.SessionId);
        Assert.Equal(tip, reset.Head);
        Assert.Equal(ImplementationOutcome.Implemented, Assert.Single(_fixture.Results).Outcome);
        Assert.Equal([StepStatus.Failed, StepStatus.Succeeded], _fixture.Steps(spec[1]).Select(step => step.Status));
    }

    [Fact]
    public async Task runner_for_a_ticket_that_is_no_longer_implementing_does_nothing()
    {
        SeededSpec spec = await _fixture.SeedRunningSpecAsync("app", (1, []));

        ImplementationResult result = await _fixture.Runner().RunAsync(new ImplementationAssignment(spec.Id, spec[1]), Token);

        Assert.Equal(ImplementationResult.NotImplementing, result);
        Assert.Empty(_fixture.Agents.Started);
        Assert.Empty(_fixture.Steps(spec[1]));
    }

    [Fact]
    public async Task newly_unblocked_tickets_start_implementer_sessions_while_another_implementer_still_runs()
    {
        SeededSpec spec = await _fixture.SeedRunningSpecAsync("app", (1, []), (2, [1]), (3, [1]), (4, [2, 3]), (5, []));
        await _fixture.ReconcileAsync(spec.Id);
        AgentRunRequest root = _fixture.Agents.InFlightIn(_fixture.Ticket(spec[1]).WorktreePath!);
        AgentRunRequest independent = _fixture.Agents.InFlightIn(_fixture.Ticket(spec[5]).WorktreePath!);

        _fixture.Agents.Reply(root, TicketExecutionFixture.Completed(_fixture.CommitInWorktree(root)));
        await _fixture.PumpEventsAsync();
        await _fixture.IntegrateAsync(spec, spec[1]);
        await _fixture.PumpEventsAsync();

        string[] inFlight = _fixture.Agents.InFlight.Select(request => request.Policy.Paths.WorkingDirectory).Order().ToArray();
        Assert.Equal(
            new[] { spec[2], spec[3], spec[5] }.Select(ticket => _fixture.Ticket(ticket).WorktreePath!).Order(),
            inFlight);
        Assert.Contains(independent, _fixture.Agents.InFlight);
        Assert.Equal(TicketRunStatus.Blocked, _fixture.Ticket(spec[4]).Status);
    }
}
