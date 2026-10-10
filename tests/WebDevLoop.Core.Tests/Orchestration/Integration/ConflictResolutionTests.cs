using WebDevLoop.Core.Agents;
using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Orchestration.Integration;
using WebDevLoop.Core.Orchestration.Results;
using WebDevLoop.Core.Orchestration.TicketExecution;
using WebDevLoop.Core.Ports;

namespace WebDevLoop.Core.Tests.Orchestration.Integration;

public sealed class ConflictResolutionTests
{
    private readonly IntegrationFixture _f = new();

    [Fact]
    public async Task Squash_conflict_starts_one_conflict_resolver_session_then_retries_the_squash()
    {
        (SpecRun spec, TicketRun ticket, CommitSha bottomLayer) = await SeedConflictingTicketAsync();
        CommitSha reviewed = ticket.LastImplementedSha!.Value;
        int callsBefore = _f.Journal.Calls.Count;
        _f.ScriptResolver(spec);

        IntegrationResult result = await _f.IntegrateAsync(ticket);

        Assert.Equal(IntegrationOutcome.Integrated, result.Outcome);
        AgentRunRequest resolver = Assert.Single(_f.Agents.Started);
        Assert.Equal(AgentRole.ConflictResolver, resolver.Role);
        Assert.Equal(2, _f.Journal.Calls.Skip(callsBefore).Count(call => call == "squash"));
        CommitSha resolved = ticket.LastImplementedSha!.Value;
        Assert.NotEqual(reviewed, resolved);
        Assert.True(_f.IsAncestor(bottomLayer, resolved));
        CommitSha squash = _f.Saga(ticket)!.SquashCommitSha!.Value;
        Assert.Equal([bottomLayer], _f.Git.ParentsOf(squash));
        Assert.Equal(["upper.cs"], _f.ChangedFiles(bottomLayer, squash));

        StepRun step = Assert.Single(await _f.Store.ListByTicketRunAsync(ticket.Id, IntegrationFixture.Token));
        Assert.Equal((StepKind.ResolveConflict, StepStatus.Succeeded, AgentRole.ConflictResolver), (step.Kind, step.Status, step.AgentRole!.Value));
        Assert.Equal(resolver.SessionId.Value, step.CopilotSessionId);
        Assert.False(string.IsNullOrWhiteSpace(step.Model));
        Assert.Equal((resolver.Settings.Model, resolver.Settings.ReasoningEffort), (step.Model, step.ReasoningEffort));
    }

    [Fact]
    public async Task Conflict_resolver_policy_allows_edits_only_in_the_assigned_ticket_worktree()
    {
        (SpecRun spec, TicketRun ticket, CommitSha bottomLayer) = await SeedConflictingTicketAsync();
        _f.UseTemplate(AgentRole.ConflictResolver, "{worktree_path}|{branch_name}|{integration_tip_sha}|{conflicting_files}|{changed_files}");
        _f.ScriptResolver(spec);

        await _f.IntegrateAsync(ticket);

        AgentRunRequest request = Assert.Single(_f.Agents.Started);
        string worktree = ticket.WorktreePath!;
        RoleCapabilityPolicy policy = request.Policy;
        Assert.Equal(AgentRole.ConflictResolver, policy.Role);
        Assert.Equal(worktree, policy.Paths.WorkingDirectory);
        Assert.Equal([worktree], policy.Paths.WritableRoots);
        Assert.True(policy.Paths.CanWrite(Path.Combine(worktree, "src", "shared.cs")));
        Assert.False(policy.Paths.CanWrite(_f.Repository.LocalPath));
        Assert.False(policy.Paths.CanWrite(Path.Combine(Path.GetDirectoryName(worktree)!, "other-ticket", "shared.cs")));
        Assert.False(policy.RequiresGitHubWriteToken);
        Assert.Equal(GitHubTokenAccess.None, policy.TokenAccess);
        Assert.False(policy.IsCommandAllowed("git push origin HEAD"));
        Assert.Equal($"{worktree}|{ticket.BranchName}|{bottomLayer}|shared.cs|upper.cs", request.Prompt);
    }

    [Fact]
    public async Task Blocked_conflict_resolution_needs_attention_without_moving_or_publishing_anything()
    {
        (SpecRun spec, TicketRun ticket, CommitSha bottomLayer) = await SeedConflictingTicketAsync();
        int callsBefore = _f.Journal.Calls.Count;
        _f.Agents.Script(AgentRole.ConflictResolver, _ => new ConflictResolutionReport(ConflictResolutionStatus.Blocked, null, [], "The two tickets contradict each other.", []));

        IntegrationResult result = await _f.IntegrateAsync(ticket);

        Assert.Equal(IntegrationOutcome.NeedsAttention, result.Outcome);
        Assert.Contains("contradict", result.Reason, StringComparison.Ordinal);
        Assert.Equal(TicketRunStatus.NeedsAttention, ticket.Status);
        Assert.Equal(AttentionCode.MergeConflictUnresolved, ticket.Attention!.Code);
        Assert.Contains(ticket.Attention.TriedSoFar, tried => tried.Contains("conflict-resolver", StringComparison.Ordinal));
        Assert.Equal(["squash"], _f.Journal.Calls.Skip(callsBefore));
        Assert.Equal(bottomLayer, _f.LocalTip(spec.IntegrationBranch));
        Assert.Single(_f.Pulls.PullRequests);
        Assert.Equal(IntegrationSagaCheckpoint.Started, _f.Saga(ticket)!.Checkpoint);
        Assert.Equal(StepStatus.Failed, Assert.Single(await _f.Store.ListByTicketRunAsync(ticket.Id, IntegrationFixture.Token)).Status);
    }

    [Fact]
    public async Task Resolution_that_does_not_contain_the_integration_tip_is_rejected()
    {
        (_, TicketRun ticket, _) = await SeedConflictingTicketAsync();
        int callsBefore = _f.Journal.Calls.Count;
        _f.Agents.Script(AgentRole.ConflictResolver, _ =>
            new ConflictResolutionReport(ConflictResolutionStatus.Resolved, ticket.LastImplementedSha, ["shared.cs"], "Nothing to merge.", []));

        IntegrationResult result = await _f.IntegrateAsync(ticket);

        Assert.Equal(IntegrationOutcome.NeedsAttention, result.Outcome);
        Assert.Contains("does not contain the integration tip", result.Reason, StringComparison.Ordinal);
        Assert.Equal(["squash"], _f.Journal.Calls.Skip(callsBefore));
    }

    [Fact]
    public async Task A_worktree_left_dirty_by_test_artefacts_is_cleaned_automatically_before_the_resolver_runs()
    {
        (SpecRun spec, TicketRun ticket, _) = await SeedConflictingTicketAsync();
        _f.Git.SetWorktreeChanges(ticket.WorktreePath!, new WorktreeChanges(string.Empty, [], ["__pycache__/calc.pyc"], []));
        _f.ScriptResolver(spec);

        IntegrationResult result = await _f.IntegrateAsync(ticket);

        Assert.Equal(IntegrationOutcome.Integrated, result.Outcome);
        Assert.Equal(AgentRole.ConflictResolver, Assert.Single(_f.Agents.Started).Role);
        RunEvent remediation = Assert.Single(await ((IRunEventRepository)_f.Store).ListBySpecRunAsync(spec.Id, IntegrationFixture.Token), e => e.Type == WorktreeRemediator.RunEventType);
        Assert.Equal(ticket.Id, remediation.TicketRunId);
    }

    /// <summary>Two tickets branched from the same base; the first is integrated, and squashing the second conflicts.</summary>
    private async Task<(SpecRun Spec, TicketRun Ticket, CommitSha BottomLayer)> SeedConflictingTicketAsync()
    {
        SpecRun spec = _f.SeedRunningSpec();
        TicketRun bottom = _f.SeedReviewedTicket(spec, 1, "bottom.cs", "shared.cs");
        TicketRun ticket = _f.SeedReviewedTicket(spec, 2, "upper.cs");
        Assert.Equal(IntegrationOutcome.Integrated, (await _f.IntegrateAsync(bottom)).Outcome);
        _f.Git.ConflictOnNextSquash = ["shared.cs"];
        return (spec, ticket, spec.IntegrationTipSha!.Value);
    }
}
