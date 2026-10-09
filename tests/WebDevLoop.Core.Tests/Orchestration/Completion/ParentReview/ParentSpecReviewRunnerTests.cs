using WebDevLoop.Core.Agents;
using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Events;
using WebDevLoop.Core.Orchestration.Completion.ParentReview;
using WebDevLoop.Core.Orchestration.Findings;
using WebDevLoop.Core.Orchestration.Results;
using WebDevLoop.Core.Ports;
using WebDevLoop.Core.Tests.Orchestration.Findings;
using WebDevLoop.Core.Tests.Orchestration.ReviewLoop;
using WebDevLoop.Core.Tests.Orchestration.TicketExecution;

namespace WebDevLoop.Core.Tests.Orchestration.Completion.ParentReview;

public sealed class ParentSpecReviewRunnerTests
{
    private const string TargetTemplate = "{review_scope} {worktree_path} {branch_name} {diff_base_ref}...{diff_head_ref}";

    private static readonly SpecificationFinding MissingLogging = ReviewLoopFixture.SpecFinding;

    private readonly ParentReviewFixture _fixture = new();

    [Fact]
    public async Task Clean_parent_review_moves_spec_to_tester_ready_state()
    {
        _fixture.UseReviewerTemplate(TargetTemplate);
        SeededSpec spec = await _fixture.SeedParentReviewingAsync();
        SpecRun before = _fixture.Spec(spec.Id);
        string checkout = ParentReviewWorkspace.For(TicketExecutionFixture.WorkspaceRoot, spec.Id).CheckoutDirectory;
        _fixture.Clean();

        ParentReviewResult result = await _fixture.RunAsync(spec.Id);

        Assert.Equal(ParentReviewOutcome.ReadyForTesting, result.Outcome);
        Assert.Equal(SpecRunStatus.Testing, _fixture.Spec(spec.Id).Status);
        SpecRunStatusChanged changed = Assert.Single(_fixture.SpecTransitions(spec.Id));
        Assert.Equal((SpecRunStatus.ParentReviewing, SpecRunStatus.Testing), (changed.From, changed.To));
        AgentRunRequest[] requests = _fixture.ReviewerRequests.ToArray();
        Assert.Equal(2, requests.Length);
        Assert.All(requests, request => Assert.Equal(
            $"parent_spec {checkout} webdevloop/{spec.Id}/parent-review {before.IntegrationBaseSha}...{before.IntegrationTipSha}",
            request.Prompt));
        Assert.All(requests, request => Assert.Equal(checkout, request.Policy.Paths.WorkingDirectory));
        Assert.Equal(2, _fixture.ParentReviewSteps(spec.Id).Count(step => step.Status == StepStatus.Succeeded && step.TicketRunId is null));
        Assert.Equal(WorktreeStatus.Missing, (await _fixture.Review.Git.InspectWorktreeAsync(spec.Location, checkout, ParentReviewFixture.Token)).Status);
        Assert.Empty(_fixture.Issues.CreatedDrafts);
    }

    [Fact]
    public async Task Ticket_placeholders_are_filled_as_not_applicable_for_the_parent_spec()
    {
        _fixture.UseReviewerTemplate("#{ticket_issue_number} {ticket_title} {ticket_body} {ticket_dependencies} {review_scope}");
        SeededSpec spec = await _fixture.SeedParentReviewingAsync();
        _fixture.Clean();

        ParentReviewResult result = await _fixture.RunAsync(spec.Id);

        Assert.Equal(ParentReviewOutcome.ReadyForTesting, result.Outcome);
        Assert.All(_fixture.ReviewerRequests, request => Assert.Equal("#n/a n/a n/a n/a parent_spec", request.Prompt));
    }

    [Fact]
    public async Task Review_finding_creates_exactly_one_sub_issue_and_ticket_and_resumes_the_frontier()
    {
        SeededSpec spec = await _fixture.SeedParentReviewingAsync();
        _fixture.SpecificationIssues(MissingLogging);

        ParentReviewResult result = await _fixture.RunAsync(spec.Id);

        Assert.Equal(ParentReviewOutcome.FindingTicketsCreated, result.Outcome);
        FindingIssueDraft draft = Assert.Single(_fixture.Issues.CreatedDrafts);
        FindingTicket ticket = Assert.Single(result.Tickets);
        Assert.Equal([ticket.Issue.Number], _fixture.Issues.SubIssueNumbers(_fixture.Spec(spec.Id).ParentIssue));
        Assert.Equal(FindingFingerprints.Compute(spec.Id, StepKind.ParentReview, MissingLogging), draft.Fingerprint);
        TicketRun created = Assert.Single(_fixture.Tickets(spec.Id), run => run.Id == ticket.TicketRunId);
        Assert.Equal(TicketRunStatus.Blocked, created.Status);
        StepRun specificationStep = Assert.Single(_fixture.ParentReviewSteps(spec.Id), step => step.AgentRole == AgentRole.ReviewerSpecification);
        Assert.Equal(specificationStep.Id, Assert.Single(_fixture.Issuances.Rows).SourceStepRunId);
        Assert.Equal(SpecRunStatus.Running, _fixture.Spec(spec.Id).Status);
        SpecRunStatusChanged changed = Assert.Single(_fixture.SpecTransitions(spec.Id));
        Assert.Equal((SpecRunStatus.ParentReviewing, SpecRunStatus.Running), (changed.From, changed.To));
    }

    [Fact]
    public async Task Reported_finding_dependencies_survive_the_persisted_review_and_become_blocking_relations()
    {
        SeededSpec spec = await _fixture.SeedParentReviewingAsync();
        _fixture.SpecificationIssues(
            FindingsFixture.Missing("Errors are not logged.", "src/Feature.cs", id: "F1"),
            FindingsFixture.Missing("Retries are missing.", "src/Other.cs", id: "F2", blockedBy: "F1"));

        ParentReviewResult result = await _fixture.RunAsync(spec.Id);

        Assert.Equal(2, result.Tickets.Count);
        TicketDependency dependency = Assert.Single(_fixture.Execution.Db.TicketDependencies, edge => edge.Source == DependencySource.CreatedFinding);
        Assert.Equal((result.Tickets[1].TicketRunId, result.Tickets[0].TicketRunId), (dependency.BlockedTicketRunId, dependency.BlockingTicketRunId));
    }

    [Fact]
    public async Task Restart_after_github_issue_creation_but_before_db_completion_reuses_the_issue()
    {
        SeededSpec spec = await _fixture.SeedParentReviewingAsync();
        _fixture.SpecificationIssues(MissingLogging);
        _fixture.Issues.CrashAfterNextCreate = true;

        await Assert.ThrowsAsync<SimulatedCrashException>(() => _fixture.RunAsync(spec.Id));
        Assert.Equal(SpecRunStatus.ParentReviewing, _fixture.Spec(spec.Id).Status);

        ParentReviewResult resumed = await _fixture.RunAsync(spec.Id);

        Assert.Equal(ParentReviewOutcome.FindingTicketsCreated, resumed.Outcome);
        Assert.Equal(2, _fixture.ReviewerRequests.Count());
        FindingIssueDraft draft = Assert.Single(_fixture.Issues.CreatedDrafts);
        Assert.Equal(draft.Fingerprint, Assert.Single(resumed.Tickets).Fingerprint);
        Assert.Equal(2, _fixture.Tickets(spec.Id).Count);
        Assert.Equal(SpecRunStatus.Running, _fixture.Spec(spec.Id).Status);
    }

    [Fact]
    public async Task Repeated_identical_finding_reuses_its_ticket_and_needs_attention_when_nothing_is_left_to_do()
    {
        SeededSpec spec = await _fixture.SeedParentReviewingAsync();
        _fixture.SpecificationIssues(MissingLogging);
        FindingTicket first = Assert.Single((await _fixture.RunAsync(spec.Id)).Tickets);
        await _fixture.WorkTicketAsync(spec, first.TicketRunId);
        await _fixture.MoveSpecAsync(spec.Id, SpecRunStatus.ParentReviewing);
        _fixture.SpecificationIssues(MissingLogging);

        ParentReviewResult repeated = await _fixture.RunAsync(spec.Id);

        Assert.Equal(ParentReviewOutcome.NoNewWork, repeated.Outcome);
        FindingTicket reused = Assert.Single(repeated.Tickets);
        Assert.Equal((FindingTicketOrigin.ExistingTicket, first.TicketRunId), (reused.Origin, reused.TicketRunId));
        Assert.Single(_fixture.Issues.CreatedDrafts);
        Assert.Equal(2, _fixture.Tickets(spec.Id).Count);
        SpecRun needsAttention = _fixture.Spec(spec.Id);
        Assert.Equal(SpecRunStatus.NeedsAttention, needsAttention.Status);
        Assert.Contains($"#{first.Issue.Number}", needsAttention.FailureReason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Findings_in_the_last_allowed_cycle_create_tickets_but_the_spec_needs_attention()
    {
        SeededSpec spec = await _fixture.SeedParentReviewingAsync();
        _fixture.Review.Settings.Configure(spec.RepositoryId, settings => settings with { ParentReviewCycleLimit = 1 });
        _fixture.SpecificationIssues(MissingLogging);

        ParentReviewResult result = await _fixture.RunAsync(spec.Id);

        Assert.Equal(ParentReviewOutcome.CycleLimitReached, result.Outcome);
        Assert.Single(_fixture.Issues.CreatedDrafts);
        Assert.Equal(TicketRunStatus.Blocked, _fixture.Tickets(spec.Id).Single(ticket => ticket.Id == result.Tickets[0].TicketRunId).Status);
        SpecRun needsAttention = _fixture.Spec(spec.Id);
        Assert.Equal(SpecRunStatus.NeedsAttention, needsAttention.Status);
        Assert.Contains("1 parent-spec review cycle(s)", needsAttention.FailureReason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Reviewer_failing_every_attempt_moves_spec_to_needs_attention()
    {
        SeededSpec spec = await _fixture.SeedParentReviewingAsync();
        _fixture.Review.Clean(FindingAxis.CodingStandards);

        ParentReviewResult result = await _fixture.RunAsync(spec.Id);

        Assert.Equal(ParentReviewOutcome.Failed, result.Outcome);
        Assert.Equal(SpecRunStatus.NeedsAttention, _fixture.Spec(spec.Id).Status);
        Assert.Contains("specification review failed", _fixture.Spec(spec.Id).FailureReason, StringComparison.Ordinal);
        Assert.Empty(_fixture.Issues.CreatedDrafts);
    }

    [Fact]
    public async Task Spec_that_is_not_parent_reviewing_is_left_alone()
    {
        SeededSpec spec = await _fixture.SeedIntegratedSpecAsync();
        _fixture.Clean();

        ParentReviewResult result = await _fixture.RunAsync(spec.Id);

        Assert.Equal(ParentReviewOutcome.NotParentReviewing, result.Outcome);
        Assert.Empty(_fixture.ReviewerRequests);
        Assert.Equal(SpecRunStatus.Running, _fixture.Spec(spec.Id).Status);
    }
}
