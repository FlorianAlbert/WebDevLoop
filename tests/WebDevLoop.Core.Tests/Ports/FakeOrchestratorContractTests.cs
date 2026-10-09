using WebDevLoop.Core.Agents;
using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Events;
using WebDevLoop.Core.Orchestration.Results;
using WebDevLoop.Core.Ports;
using WebDevLoop.Core.Tests.Ports.Fakes;

namespace WebDevLoop.Core.Tests.Ports;

/// <summary>Drives one ticket through steps 1–7 of the workflow using only Core contracts and in-memory fakes.</summary>
public sealed class FakeOrchestratorContractTests
{
    private static readonly DateTimeOffset T0 = new(2026, 1, 1, 10, 0, 0, TimeSpan.Zero);
    private static readonly GitHubRepoRef Repo = new("octo", "app");
    private static readonly BranchName Trunk = new("main");
    private static readonly IssueRef SpecIssue = new("octo", "app", 1);
    private static readonly IssueRef TicketIssue = new("octo", "app", 2);

    private readonly FakeClock _clock = new(T0);
    private readonly SequentialIdGenerator _ids = new();
    private readonly InMemoryGitHubIssues _issues = new();
    private readonly InMemoryGitWorkspace _git = new();
    private readonly InMemoryWorkflowStore _store = new();
    private readonly ScriptedAgentRunner _agents = new();
    private readonly InMemoryPullsAndStacks _pulls;
    private readonly SingleTicketOrchestrator _orchestrator;

    public FakeOrchestratorContractTests()
    {
        _pulls = new InMemoryPullsAndStacks(branch => _git.RemoteTip(branch)!.Value);
        _orchestrator = new SingleTicketOrchestrator(_clock, _ids, _issues, _pulls, _git, _agents, _store);
        _issues.Seed(SpecIssue, "Spec");
        _issues.Seed(TicketIssue, "Ticket", parent: SpecIssue);
        _git.SeedRemoteBranch(Trunk, "README.md");
    }

    [Fact]
    public async Task clean_ticket_is_squashed_published_as_draft_stack_layer_and_closed()
    {
        ScriptCleanImplementation();
        _agents.Script(AgentRole.ReviewerCodingStandards, _ => ReviewReport.Clean(FindingAxis.CodingStandards, "ok"));
        _agents.Script(AgentRole.ReviewerSpecification, _ => ReviewReport.Clean(FindingAxis.Specification, "ok"));

        (SpecRun spec, TicketRun ticket) = await _orchestrator.RunAsync(Repository(), SpecIssue, TestContext.Current.CancellationToken);

        Assert.Equal(TicketRunStatus.Integrated, ticket.Status);
        PullRequestSnapshot pull = Assert.Single(_pulls.PullRequests);
        Assert.True(pull.IsDraft);
        Assert.Equal(RunScopedNaming.StackBranch(spec.Id, ticket.Id), pull.Head);
        Assert.Equal(Trunk, pull.Base);
        Assert.Contains(spec.Id.Value, pull.Body);
        Assert.Contains(ticket.Id.Value, pull.Body);
        Assert.Equal(spec.IntegrationTipSha, _git.RemoteTip(spec.IntegrationBranch));
        Assert.Equal(IssueState.Closed, (await _issues.GetIssueAsync(TicketIssue, TestContext.Current.CancellationToken)).State);
        Assert.Equal([pull.Number], Assert.Single(_pulls.Stacks).BottomToTop);
        Assert.Contains(_store.PendingEvents, e => e is TicketRunStatusChanged { To: TicketRunStatus.Integrated });
        Assert.All(_agents.Started, request => Assert.Equal(new AgentSessionId($"session-{request.StepRunId}"), request.SessionId));
    }

    [Fact]
    public async Task malformed_review_report_is_rejected_by_the_contract_and_stops_integration()
    {
        ScriptCleanImplementation();
        _agents.Script(AgentRole.ReviewerCodingStandards, _ =>
            new ReviewReport(FindingAxis.CodingStandards, ReviewVerdict.Clean, [new Finding("Typo", "Misspelled name")], "clean?"));

        (_, TicketRun ticket) = await _orchestrator.RunAsync(Repository(), SpecIssue, TestContext.Current.CancellationToken);

        Assert.Equal(TicketRunStatus.NeedsAttention, ticket.Status);
        Assert.Contains(nameof(AgentRunOutcome.InvalidReport), ticket.FailureReason);
        Assert.Empty(_pulls.PullRequests);
    }

    private void ScriptCleanImplementation() =>
        _agents.Script(AgentRole.Implementer, request =>
            ImplementationReport.Implemented(_git.CommitInWorktree(request.Policy.Paths.WorkingDirectory, "src/Feature.cs"), "implemented"));

    private static RepositoryRecord Repository() =>
        RepositoryRecord.Register(Repo, Trunk, "https://github.com/octo/app.git", "/work/octo/app", T0);

    private sealed class SingleTicketOrchestrator(
        IClock clock,
        IIdGenerator ids,
        IGitHubIssues issues,
        IGitHubPullsAndStacks pulls,
        IGitWorkspace git,
        IAgentRunner agents,
        InMemoryWorkflowStore store)
    {
        private static readonly AgentModelSettings Model = new("model", "medium", TimeSpan.FromMinutes(30));
        private static readonly AgentRole[] Reviewers = [AgentRole.ReviewerCodingStandards, AgentRole.ReviewerSpecification];

        private readonly ISpecRunRepository _specs = store;
        private readonly ITicketRunRepository _tickets = store;
        private readonly IStepRunRepository _steps = store;
        private readonly IIntegrationSagaRepository _sagas = store;
        private readonly IPullStackLayerRepository _layers = store;
        private readonly IOutbox _outbox = store;
        private readonly IUnitOfWork _unitOfWork = store;

        public async Task<(SpecRun Spec, TicketRun Ticket)> RunAsync(RepositoryRecord repository, IssueRef specIssue, CancellationToken ct)
        {
            SpecIssueGraph graph = await issues.GetSpecGraphAsync(specIssue, ct);
            SpecRun spec = SpecRun.Queue(ids.NewRunId(), repository.Id, graph.Spec.Ref, graph.Spec.Title, graph.Spec.Body, 1, clock.UtcNow);
            _specs.Add(spec);
            spec.TransitionTo(SpecRunStatus.Preparing, clock.UtcNow);

            GitRepositoryLocation location = GitRepositoryLocation.From(repository);
            await git.EnsureClonedAsync(location, ct);
            CommitSha trunkTip = await git.GetBranchTipAsync(location, repository.DefaultBaseBranch, GitRefScope.Remote, ct)
                ?? throw new InvalidOperationException("Trunk is missing.");
            Require((await git.UpdateBranchAsync(location, spec.IntegrationBranch, trunkTip, null, ct)).Succeeded);
            spec.IntegrationBaseSha = spec.IntegrationTipSha = trunkTip;
            spec.TransitionTo(SpecRunStatus.Running, clock.UtcNow);

            IssueSnapshot ticketIssue = Assert.Single(graph.Tickets);
            TicketRun ticket = TicketRun.Create(ids.NewTicketRunId(), spec.Id, ticketIssue.Ref, ticketIssue.Title, ticketIssue.Body, clock.UtcNow);
            _tickets.Add(ticket);
            ticket.TransitionTo(TicketRunStatus.Ready, clock.UtcNow);
            ticket.TransitionTo(TicketRunStatus.Implementing, clock.UtcNow);
            Require(await _unitOfWork.SaveChangesAsync(ct) == SaveOutcome.Saved);

            TicketWorktree worktree = await git.PrepareWorktreeAsync(location, new WorktreeSpec(ticket.BranchName, trunkTip, $"/work/trees/{ticket.Id}"), ct);
            ticket.WorktreePath = worktree.Path;

            AgentRunResult implementation = await RunStepAsync(spec, ticket, StepKind.Implement, AgentRole.Implementer, worktree.Path, repository.Ref, ct);
            if (implementation.Report is not ImplementationReport { Commit: { } implemented }
                || !await git.IsAncestorAsync(location, trunkTip, implemented, ct))
            {
                return Fail(spec, ticket, implementation);
            }

            ticket.LastImplementedSha = implemented;
            ticket.TransitionTo(TicketRunStatus.Reviewing, clock.UtcNow);
            foreach (AgentRole reviewer in Reviewers)
            {
                AgentRunResult review = await RunStepAsync(spec, ticket, StepKind.Review, reviewer, worktree.Path, repository.Ref, ct);
                if (review.Report is not ReviewReport { Verdict: ReviewVerdict.Clean })
                {
                    return Fail(spec, ticket, review);
                }
            }

            ticket.TransitionTo(TicketRunStatus.Integrating, clock.UtcNow);
            await IntegrateAsync(repository, location, spec, ticket, implemented, ct);
            return (spec, ticket);
        }

        private async Task IntegrateAsync(RepositoryRecord repository, GitRepositoryLocation location, SpecRun spec, TicketRun ticket, CommitSha source, CancellationToken ct)
        {
            CommitSha priorTip = spec.IntegrationTipSha!.Value;
            IntegrationSaga saga = IntegrationSaga.Start(spec.Id, ticket.Id, priorTip, clock.UtcNow);
            _sagas.Add(saga);

            GitMergeResult squash = await git.CreateSquashCommitAsync(location, new SquashRequest(source, priorTip, ticket.Title), ct);
            CommitSha squashSha = squash.Commit ?? throw new InvalidOperationException("Unexpected conflict.");
            saga.SquashCommitSha = squashSha;
            saga.AdvanceTo(IntegrationSagaCheckpoint.SquashCommitCreated, clock.UtcNow);

            Require((await git.UpdateBranchAsync(location, spec.IntegrationBranch, squashSha, priorTip, ct)).Succeeded);
            spec.IntegrationTipSha = squashSha;
            saga.AdvanceTo(IntegrationSagaCheckpoint.IntegrationRefUpdated, clock.UtcNow);

            Require(await git.PushAsync(location, new RefPush(spec.IntegrationBranch, squashSha, null), ct) != PushOutcome.Rejected);
            saga.AdvanceTo(IntegrationSagaCheckpoint.IntegrationPushed, clock.UtcNow);
            Require(await git.PushAsync(location, new RefPush(saga.StackBranchName, squashSha, null), ct) != PushOutcome.Rejected);
            saga.AdvanceTo(IntegrationSagaCheckpoint.StackBranchPushed, clock.UtcNow);

            PullRequestSnapshot pull = await pulls.FindPullRequestByHeadAsync(repository.Ref, saga.StackBranchName, ct)
                ?? await pulls.CreateDraftPullRequestAsync(
                    repository.Ref,
                    new DraftPullRequest(saga.StackBranchName, repository.DefaultBaseBranch, ticket.Title, $"run={spec.Id} ticket={ticket.Id}"),
                    ct);
            saga.PullRequestNumber = pull.Number;
            saga.AdvanceTo(IntegrationSagaCheckpoint.PrCreated, clock.UtcNow);

            PullStackSnapshot stack = await pulls.FindStackAsync(repository.Ref, pull.Number, ct)
                ?? await pulls.CreateStackAsync(repository.Ref, [pull.Number], ct);
            saga.StackNumber = stack.StackNumber;
            saga.AdvanceTo(IntegrationSagaCheckpoint.StackLinked, clock.UtcNow);

            Require((await git.GetChangedFilesAsync(location, priorTip, squashSha, ct)).Count > 0);
            saga.AdvanceTo(IntegrationSagaCheckpoint.DiffVerified, clock.UtcNow);

            await issues.CloseAsync(ticket.Issue, IssueCloseReason.Completed, ct);
            saga.AdvanceTo(IntegrationSagaCheckpoint.IssueTransitioned, clock.UtcNow);

            _layers.Add(PullStackLayer.Create(spec.Id, ticket.Id, saga.StackBranchName, squashSha, pull.Number, repository.DefaultBaseBranch, 1, clock.UtcNow));
            ticket.IntegratedCommitSha = squashSha;
            ticket.TransitionTo(TicketRunStatus.Integrated, clock.UtcNow);
            saga.AdvanceTo(IntegrationSagaCheckpoint.Completed, clock.UtcNow);
            _outbox.Append(new TicketRunStatusChanged(spec.Id, ticket.Id, TicketRunStatus.Integrating, TicketRunStatus.Integrated, clock.UtcNow));
            Require(await _unitOfWork.SaveChangesAsync(ct) == SaveOutcome.Saved);
        }

        private async Task<AgentRunResult> RunStepAsync(
            SpecRun spec,
            TicketRun ticket,
            StepKind kind,
            AgentRole role,
            string worktreePath,
            GitHubRepoRef repo,
            CancellationToken ct)
        {
            StepRun step = StepRun.Create(ids.NewStepRunId(), spec.Id, ticket.Id, kind, role, ticket.Attempt, "prompt-hash");
            AgentSessionId sessionId = ids.NewAgentSessionId(step.Id);
            step.CopilotSessionId = sessionId.Value;
            step.Start(clock.UtcNow, Model.Timeout);
            _steps.Add(step);
            Require(await _unitOfWork.SaveChangesAsync(ct) == SaveOutcome.Saved);

            RoleCapabilityPolicy policy = RoleCapabilityPolicies.For(role, new AgentWorkspace(worktreePath));
            AgentRunResult result = await agents.StartAsync(new AgentRunRequest(step.Id, sessionId, repo, Model, $"Work on {ticket.Title}", policy), ct);
            step.Finish(result.Outcome == AgentRunOutcome.Reported ? StepStatus.Succeeded : StepStatus.Failed, clock.UtcNow, failureReason: result.FailureReason);
            _outbox.Append(new StepRunStatusChanged(spec.Id, ticket.Id, step.Id, step.Status, clock.UtcNow));
            return result;
        }

        private (SpecRun, TicketRun) Fail(SpecRun spec, TicketRun ticket, AgentRunResult result)
        {
            ticket.MarkNeedsAttention($"{result.Outcome}: {result.FailureReason}", clock.UtcNow);
            return (spec, ticket);
        }

        private static void Require(bool condition)
        {
            if (!condition)
            {
                throw new InvalidOperationException("Fake orchestration precondition failed.");
            }
        }
    }
}
