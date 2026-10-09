using WebDevLoop.Core.Agents;
using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Orchestration.TicketExecution;
using WebDevLoop.Core.Ports;

namespace WebDevLoop.Core.Tests.Orchestration.TicketExecution;

public sealed class ImplementerResumeTests
{
    private static readonly AgentSessionId Interrupted = new("session-interrupted");

    private readonly TicketExecutionFixture _fixture = new();

    [Fact]
    public async Task resuming_continues_the_interrupted_session_in_its_preserved_worktree()
    {
        (SeededSpec spec, TicketRun ticket) = await SeedImplementingAsync();
        CommitSha partial = await PreparePartialWorkAsync(spec, ticket);
        await AddInterruptedStepAsync(spec, ticket);
        _fixture.Agents.AutoReply = request => TicketExecutionFixture.Completed(_fixture.CommitInWorktree(request, "rest.cs"));

        ImplementationResult result = await RunAsync(spec, ticket, Interrupted);

        Assert.Equal(ImplementationResult.Implemented, result);
        Assert.Empty(_fixture.Agents.Started);
        AgentRunRequest resumed = Assert.Single(_fixture.Agents.Resumed);
        Assert.Equal(Interrupted, resumed.SessionId);
        Assert.Equal(ticket.WorktreePath, resumed.Policy.Paths.WorkingDirectory);
        Assert.Contains("restarted", resumed.Prompt, StringComparison.OrdinalIgnoreCase);
        StepRun step = _fixture.Steps(ticket.Id)[^1];
        Assert.Equal((2, StepStatus.Succeeded, Interrupted.Value), (step.Attempt, step.Status, step.CopilotSessionId));
        TicketRun reviewed = _fixture.Ticket(ticket.Id);
        Assert.Equal(TicketRunStatus.Reviewing, reviewed.Status);
        Assert.True(await _fixture.Git.IsAncestorAsync(spec.Location, partial, reviewed.LastImplementedSha!.Value, TicketExecutionFixture.Token));
    }

    [Fact]
    public async Task a_missing_session_restarts_the_implementer_in_a_fresh_session_on_the_same_assigned_branch()
    {
        (SeededSpec spec, TicketRun ticket) = await SeedImplementingAsync();
        CommitSha partial = await PreparePartialWorkAsync(spec, ticket);
        await AddInterruptedStepAsync(spec, ticket);
        _fixture.Agents.MissingSessions.Add(Interrupted);
        _fixture.Agents.AutoReply = request => TicketExecutionFixture.Completed(_fixture.CommitInWorktree(request, "fresh.cs"));

        ImplementationResult result = await RunAsync(spec, ticket, Interrupted);

        Assert.Equal(ImplementationResult.Implemented, result);
        Assert.Equal(Interrupted, Assert.Single(_fixture.Agents.Resumed).SessionId);
        AgentRunRequest fresh = Assert.Single(_fixture.Agents.Started);
        Assert.NotEqual(Interrupted, fresh.SessionId);
        Assert.Equal(ticket.WorktreePath, fresh.Policy.Paths.WorkingDirectory);
        StepRun step = Assert.Single(_fixture.Steps(ticket.Id), candidate => candidate.Status == StepStatus.Succeeded);
        Assert.Equal((fresh.SessionId.Value, ticket.BranchName), (step.CopilotSessionId, step.BranchName));
        TicketRun reviewed = _fixture.Ticket(ticket.Id);
        Assert.Equal((TicketRunStatus.Reviewing, ticket.BranchName), (reviewed.Status, reviewed.BranchName));
        Assert.False(await _fixture.Git.IsAncestorAsync(spec.Location, partial, reviewed.LastImplementedSha!.Value, TicketExecutionFixture.Token));
    }

    [Fact]
    public async Task a_missing_worktree_starts_a_fresh_session_instead_of_resuming()
    {
        (SeededSpec spec, TicketRun ticket) = await SeedImplementingAsync();
        await AddInterruptedStepAsync(spec, ticket);
        _fixture.Agents.AutoReply = request => TicketExecutionFixture.Completed(_fixture.CommitInWorktree(request, "fresh.cs"));

        ImplementationResult result = await RunAsync(spec, ticket, Interrupted);

        Assert.Equal(ImplementationResult.Implemented, result);
        Assert.Empty(_fixture.Agents.Resumed);
        Assert.NotEqual(Interrupted, Assert.Single(_fixture.Agents.Started).SessionId);
    }

    private async Task<(SeededSpec Spec, TicketRun Ticket)> SeedImplementingAsync()
    {
        SeededSpec spec = await _fixture.SeedRunningSpecAsync("app", (1, []));
        await _fixture.ReconcileAsync(spec.Id);
        TicketRun ticket = _fixture.Ticket(spec[1]);
        Assert.Equal(TicketRunStatus.Implementing, ticket.Status);
        return (spec, ticket);
    }

    /// <summary>The interrupted implementer's worktree: the ticket branch on the integration tip plus a partial commit.</summary>
    private async Task<CommitSha> PreparePartialWorkAsync(SeededSpec spec, TicketRun ticket)
    {
        CommitSha tip = _fixture.Spec(spec.Id).IntegrationTipSha!.Value;
        await _fixture.Git.PrepareWorktreeAsync(spec.Location, new WorktreeSpec(ticket.BranchName, tip, ticket.WorktreePath!), TicketExecutionFixture.Token);
        return _fixture.Git.CommitInWorktree(ticket.WorktreePath!, "partial.cs");
    }

    private async Task AddInterruptedStepAsync(SeededSpec spec, TicketRun ticket)
    {
        StepRun step = StepRun.Create(new StepRunId("step-interrupted"), spec.Id, ticket.Id, StepKind.Implement, AgentRole.Implementer, 1, "hash");
        step.CopilotSessionId = Interrupted.Value;
        step.Start(TicketExecutionFixture.T0, TimeSpan.FromHours(1));
        step.Finish(StepStatus.Failed, TicketExecutionFixture.T0, failureReason: "Interrupted by a restart.");
        CasWorkflowScope scope = _fixture.Db.OpenScope();
        scope.Add(step);
        Assert.Equal(SaveOutcome.Saved, await scope.SaveChangesAsync(TicketExecutionFixture.Token));
    }

    private Task<ImplementationResult> RunAsync(SeededSpec spec, TicketRun ticket, AgentSessionId resume) =>
        _fixture.Runner().RunAsync(new ImplementationAssignment(spec.Id, ticket.Id, resume), TicketExecutionFixture.Token);
}
