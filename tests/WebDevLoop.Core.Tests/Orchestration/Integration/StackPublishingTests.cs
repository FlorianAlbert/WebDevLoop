using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Orchestration.Integration;
using WebDevLoop.Core.Ports;

namespace WebDevLoop.Core.Tests.Orchestration.Integration;

public sealed class StackPublishingTests
{
    private readonly IntegrationFixture _f = new();

    [Fact]
    public async Task Clean_ticket_becomes_one_squash_commit_on_the_integration_tip_published_as_the_bottom_pull_request()
    {
        SpecRun spec = _f.SeedRunningSpec();
        TicketRun ticket = _f.SeedReviewedTicket(spec, 1, "feature.cs");

        IntegrationResult result = await _f.IntegrateAsync(ticket);

        Assert.Equal(IntegrationOutcome.Integrated, result.Outcome);
        CommitSha squash = _f.LocalTip(spec.IntegrationBranch);
        Assert.Equal(_f.TrunkTip, ParentOf(squash));
        Assert.Equal(["feature.cs"], _f.ChangedFiles(_f.TrunkTip, squash));
        Assert.Equal(squash, spec.IntegrationTipSha);
        Assert.Equal(squash, ticket.IntegratedCommitSha);
        Assert.Equal(squash, _f.Git.RemoteTip(spec.IntegrationBranch));
        Assert.Equal(squash, _f.Git.RemoteTip(RunScopedNaming.StackBranch(spec.Id, ticket.Id)));

        PullRequestSnapshot pull = Assert.Single(_f.Pulls.PullRequests);
        Assert.Equal(RunScopedNaming.StackBranch(spec.Id, ticket.Id), pull.Head);
        Assert.Equal(IntegrationFixture.Trunk, pull.Base);
        Assert.True(pull.IsDraft);
        Assert.Empty(_f.Pulls.Stacks);

        PullStackLayer layer = Assert.Single(_f.Layers(spec));
        Assert.Equal((1, pull.Number, squash, squash), (layer.Position, layer.PullRequestNumber, layer.CommitSha, layer.VerifiedDiffSha!.Value));
        Assert.Equal(pull.Number, ticket.PullRequestNumber);
        Assert.Equal(1, ticket.StackPosition);

        Assert.Equal(IssueState.Closed, _f.IssueState(ticket));
        Assert.Equal(TicketRunStatus.Integrated, ticket.Status);
        Assert.Single(_f.Transitions(ticket, TicketRunStatus.Integrated));
        Assert.True(_f.Saga(ticket)!.IsCompleted);
        Assert.Empty(_f.Agents.Started);
    }

    [Fact]
    public async Task Two_independent_tickets_finishing_out_of_order_form_a_linear_stack_in_integration_order()
    {
        SpecRun spec = _f.SeedRunningSpec();
        TicketRun first = _f.SeedReviewedTicket(spec, 1, "first.cs");
        TicketRun second = _f.SeedReviewedTicket(spec, 2, "second.cs");

        Assert.Equal(IntegrationOutcome.Integrated, (await _f.IntegrateAsync(second)).Outcome);
        Assert.Equal(IntegrationOutcome.Integrated, (await _f.IntegrateAsync(first)).Outcome);

        IReadOnlyList<PullStackLayer> layers = _f.Layers(spec);
        Assert.Equal([second.Id, first.Id], layers.Select(layer => layer.TicketRunId));
        Assert.Equal([1, 2], layers.Select(layer => layer.Position));
        Assert.Equal(_f.TrunkTip, ParentOf(layers[0].CommitSha));
        Assert.Equal(layers[0].CommitSha, ParentOf(layers[1].CommitSha));
        Assert.Equal(["second.cs"], _f.ChangedFiles(_f.TrunkTip, layers[0].CommitSha));
        Assert.Equal(["first.cs"], _f.ChangedFiles(layers[0].CommitSha, layers[1].CommitSha));
        Assert.Equal(layers[1].CommitSha, _f.LocalTip(spec.IntegrationBranch));

        PullRequestSnapshot bottom = await _f.Pulls.GetPullRequestAsync(IntegrationFixture.RepoRef, layers[0].PullRequestNumber, IntegrationFixture.Token);
        PullRequestSnapshot top = await _f.Pulls.GetPullRequestAsync(IntegrationFixture.RepoRef, layers[1].PullRequestNumber, IntegrationFixture.Token);
        Assert.Equal(IntegrationFixture.Trunk, bottom.Base);
        Assert.Equal(layers[0].BranchName, top.Base);
        PullStackSnapshot stack = Assert.Single(_f.Pulls.Stacks);
        Assert.Equal([bottom.Number, top.Number], stack.BottomToTop);
        Assert.All(layers, layer => Assert.Equal(stack.StackNumber, layer.StackNumber));
    }

    [Fact]
    public async Task Concurrent_integrations_in_one_repository_run_one_at_a_time()
    {
        SpecRun spec = _f.SeedRunningSpec();
        TicketRun first = _f.SeedReviewedTicket(spec, 1, "first.cs");
        TicketRun second = _f.SeedReviewedTicket(spec, 2, "second.cs");
        IntegrationFixture.MoveToIntegrating(first);
        IntegrationFixture.MoveToIntegrating(second);
        TaskCompletionSource firstSquash = _f.Journal.Hold("squash");

        Task<IntegrationResult> running = _f.Runner().RunAsync(_f.AssignmentFor(first), IntegrationFixture.Token);
        Task<IntegrationResult> waiting = _f.Runner().RunAsync(_f.AssignmentFor(second), IntegrationFixture.Token);

        Assert.Single(_f.Journal.Started, call => call == "squash");
        Assert.False(running.IsCompleted);
        Assert.False(waiting.IsCompleted);

        firstSquash.SetResult();
        IntegrationResult[] results = await Task.WhenAll(running, waiting);

        Assert.All(results, result => Assert.Equal(IntegrationOutcome.Integrated, result.Outcome));
        IReadOnlyList<PullStackLayer> layers = _f.Layers(spec);
        Assert.Equal([first.Id, second.Id], layers.Select(layer => layer.TicketRunId));
        Assert.Equal(layers[0].CommitSha, ParentOf(layers[1].CommitSha));
    }

    [Fact]
    public async Task Pull_request_carries_the_run_and_ticket_identifiers_and_references_the_ticket_without_a_closing_keyword()
    {
        SpecRun spec = _f.SeedRunningSpec();
        TicketRun ticket = _f.SeedReviewedTicket(spec, 7, "feature.cs");

        await _f.IntegrateAsync(ticket);

        DraftPullRequest draft = Assert.Single(_f.JournaledPulls.Drafts);
        Assert.Equal((spec.Id, ticket.Id), (draft.RunId, draft.TicketRunId));
        Assert.Equal(ticket.Title, draft.Title);
        Assert.Contains(spec.Id.Value, draft.Body, StringComparison.Ordinal);
        Assert.Contains(ticket.Id.Value, draft.Body, StringComparison.Ordinal);
        Assert.Contains("#7", draft.Body, StringComparison.Ordinal);
        Assert.Contains($"#{spec.ParentIssue.Number}", draft.Body, StringComparison.Ordinal);
        Assert.DoesNotMatch(@"(?i)\b(close[sd]?|fix(e[sd])?|resolve[sd]?)\s+#", draft.Body);
    }

    [Fact]
    public async Task Stack_on_top_spec_publishes_its_first_layer_on_the_blocking_specs_top_layer_and_joins_its_stack()
    {
        SpecRun blocker = _f.SeedRunningSpec();
        TicketRun blockerTicket = _f.SeedReviewedTicket(blocker, 1, "blocker.cs");
        await _f.IntegrateAsync(blockerTicket);
        PullStackLayer blockerTop = Assert.Single(_f.Layers(blocker));

        SpecRun dependent = _f.SeedRunningSpec(blocker.IntegrationTipSha, SpecDependencyMode.StackOnTop);
        _f.Store.AddDependency(SpecDependency.OnSpecRun(dependent.Id, blocker.Id, SpecDependencyMode.StackOnTop));
        TicketRun ticket = _f.SeedReviewedTicket(dependent, 2, "dependent.cs");

        IntegrationResult result = await _f.IntegrateAsync(ticket);

        Assert.Equal(IntegrationOutcome.Integrated, result.Outcome);
        PullStackLayer layer = Assert.Single(_f.Layers(dependent));
        Assert.Equal(blockerTop.BranchName, layer.BaseBranch);
        Assert.Equal(blockerTop.CommitSha, ParentOf(layer.CommitSha));
        Assert.Equal(IntegrationFixture.Trunk, dependent.BaseBranch);
        PullRequestSnapshot pull = await _f.Pulls.GetPullRequestAsync(IntegrationFixture.RepoRef, layer.PullRequestNumber, IntegrationFixture.Token);
        Assert.Equal(blockerTop.BranchName, pull.Base);
        PullStackSnapshot stack = Assert.Single(_f.Pulls.Stacks);
        Assert.Equal([blockerTop.PullRequestNumber, layer.PullRequestNumber], stack.BottomToTop);
    }

    private CommitSha ParentOf(CommitSha commit) => Assert.Single(_f.Git.ParentsOf(commit));
}
