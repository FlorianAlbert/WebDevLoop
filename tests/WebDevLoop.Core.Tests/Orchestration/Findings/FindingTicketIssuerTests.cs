using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Orchestration.Findings;
using WebDevLoop.Core.Ports;
using WebDevLoop.Core.Tests.Orchestration.TicketExecution;

namespace WebDevLoop.Core.Tests.Orchestration.Findings;

public sealed class FindingTicketIssuerTests
{
    private readonly FindingsFixture _fixture = new();

    private async Task<SeededSpec> SeedAsync() => await _fixture.Execution.SeedRunningSpecAsync("app", (2, []));

    [Fact]
    public async Task Finding_creates_one_sub_issue_with_issuance_record_and_blocked_ticket_run()
    {
        SeededSpec spec = await SeedAsync();
        SourcedFinding finding = FindingsFixture.ParentReview(FindingsFixture.Missing("Errors are not logged."));
        FindingFingerprint fingerprint = FindingFingerprints.Compute(spec.Id, StepKind.ParentReview, finding.Finding);

        FindingIssuanceResult result = await _fixture.IssueAsync(spec.Id, finding);

        Assert.Equal(FindingIssuanceOutcome.Issued, result.Outcome);
        FindingIssueDraft draft = Assert.Single(_fixture.Issues.CreatedDrafts);
        Assert.Equal(fingerprint, draft.Fingerprint);
        Assert.Equal(1, draft.Parent.Number);
        Assert.Equal("Errors are not logged.", draft.Title);
        Assert.Contains("Errors must be logged.", draft.Body, StringComparison.Ordinal);
        Assert.Contains("Implement it as specified.", draft.Body, StringComparison.Ordinal);
        Assert.Contains("src/Feature.cs", draft.Body, StringComparison.Ordinal);
        Assert.Contains(spec.Id.Value, draft.Body, StringComparison.Ordinal);
        FindingTicket ticket = Assert.Single(result.Tickets);
        Assert.Equal([ticket.Issue.Number], _fixture.Issues.SubIssueNumbers(draft.Parent));
        Assert.Equal(FindingTicketOrigin.NewTicket, ticket.Origin);
        FindingIssuance issuance = Assert.Single(_fixture.Issuances.Rows);
        Assert.Equal(
            (FindingIssuanceStatus.Created, ticket.Issue.Number, ticket.Issue.DatabaseId, FindingsFixture.SpecificationStep, FindingAxis.Specification, fingerprint),
            (issuance.Status, issuance.IssueNumber!.Value, issuance.IssueDatabaseId, issuance.SourceStepRunId, issuance.Axis, issuance.Fingerprint));
        TicketRun run = _fixture.Ticket(ticket.TicketRunId);
        Assert.Equal((spec.Id, ticket.Issue.Number, TicketRunStatus.Blocked, draft.Title, draft.Body), (run.SpecRunId, run.Issue.Number, run.Status, run.Title, run.BodySnapshot));
        Assert.Empty(result.AddedDependencies);
        Assert.Empty(_fixture.FindingDependencies(spec.Id));
    }

    [Fact]
    public async Task Findings_in_the_same_file_are_serialized_by_one_blocking_relation()
    {
        SeededSpec spec = await SeedAsync();

        FindingIssuanceResult result = await _fixture.IssueAsync(
            spec.Id,
            FindingsFixture.ParentReview(FindingsFixture.MagicNumber("src/Feature.cs")),
            FindingsFixture.ParentReview(FindingsFixture.Missing("Errors are not logged.", "src/Other.cs")),
            FindingsFixture.ParentReview(FindingsFixture.Missing("Retries are missing.", "src/Feature.cs", 40)));

        Assert.Equal(3, _fixture.Issues.CreatedDrafts.Count);
        (FindingTicket first, FindingTicket other, FindingTicket third) = (result.Tickets[0], result.Tickets[1], result.Tickets[2]);
        var expected = new DependencyEdge<TicketRunId>(third.TicketRunId, first.TicketRunId);
        Assert.Equal([expected], result.AddedDependencies);
        Assert.Equal([(third.Issue.Number, first.Issue.Number)], _fixture.Issues.BlockedByCalls.Select(call => (call.Blocked.Number, call.Blocking.Number)));
        TicketDependency dependency = Assert.Single(_fixture.FindingDependencies(spec.Id));
        Assert.Equal(expected, dependency.ToEdge());
        Assert.DoesNotContain(_fixture.Execution.Db.TicketDependencies, edge => edge.BlockedTicketRunId == other.TicketRunId || edge.BlockingTicketRunId == other.TicketRunId);
    }

    [Fact]
    public async Task Repeated_identical_finding_reuses_its_ticket()
    {
        SeededSpec spec = await SeedAsync();
        FindingIssuanceResult first = await _fixture.IssueAsync(spec.Id, FindingsFixture.ParentReview(FindingsFixture.Missing("Errors are not logged.")));

        SourcedFinding again = new(StepKind.ParentReview, new StepRunId("parent-review-specification-2"), FindingsFixture.Missing("errors are  NOT logged."));
        FindingIssuanceResult second = await _fixture.IssueAsync(spec.Id, again, again);

        Assert.Single(_fixture.Issues.CreatedDrafts);
        FindingTicket reused = Assert.Single(second.Tickets);
        Assert.Equal(FindingTicketOrigin.ExistingTicket, reused.Origin);
        Assert.Equal((first.Tickets[0].TicketRunId, first.Tickets[0].Issue.Number), (reused.TicketRunId, reused.Issue.Number));
        Assert.Single(_fixture.Issuances.Rows);
        Assert.Equal(2, _fixture.Tickets(spec.Id).Count);
    }

    [Fact]
    public async Task Restart_after_issue_creation_but_before_db_completion_reuses_the_existing_issue()
    {
        SeededSpec spec = await SeedAsync();
        SourcedFinding finding = FindingsFixture.ParentReview(FindingsFixture.Missing("Errors are not logged."));
        _fixture.Issues.BeforeCreate = draft => Assert.Equal(
            FindingIssuanceStatus.Planned,
            Assert.Single(_fixture.Issuances.Rows, row => row.Fingerprint == draft.Fingerprint).Status);
        _fixture.Issues.CrashAfterNextCreate = true;

        await Assert.ThrowsAsync<SimulatedCrashException>(() => _fixture.IssueAsync(spec.Id, finding));
        Assert.Equal(FindingIssuanceStatus.Planned, Assert.Single(_fixture.Issuances.Rows).Status);
        Assert.Single(_fixture.Tickets(spec.Id));

        FindingIssuanceResult resumed = await _fixture.IssueAsync(spec.Id, finding);

        FindingIssueDraft created = Assert.Single(_fixture.Issues.CreatedDrafts);
        FindingTicket ticket = Assert.Single(resumed.Tickets);
        Assert.Equal(created.Fingerprint, ticket.Fingerprint);
        Assert.Equal(FindingIssuanceStatus.Created, Assert.Single(_fixture.Issuances.Rows).Status);
        Assert.Equal(ticket.Issue.Number, Assert.Single(_fixture.Issuances.Rows).IssueNumber);
        Assert.Single(_fixture.Tickets(spec.Id), run => run.Issue.Number == ticket.Issue.Number);
        Assert.True(_fixture.Issues.FindCalls >= 1);
    }

    [Fact]
    public async Task Restart_after_planning_but_before_issue_creation_creates_the_issue_once()
    {
        SeededSpec spec = await SeedAsync();
        SourcedFinding finding = FindingsFixture.ParentReview(FindingsFixture.Missing("Errors are not logged."));
        _fixture.Issues.FailBeforeNextCreate = true;
        await Assert.ThrowsAsync<SimulatedCrashException>(() => _fixture.IssueAsync(spec.Id, finding));

        FindingIssuanceResult resumed = await _fixture.IssueAsync(spec.Id, finding);

        Assert.Single(_fixture.Issues.CreatedDrafts);
        Assert.Equal(FindingTicketOrigin.NewTicket, Assert.Single(resumed.Tickets).Origin);
        Assert.Equal(FindingIssuanceStatus.Created, Assert.Single(_fixture.Issuances.Rows).Status);
    }

    [Fact]
    public async Task Blocking_relation_that_would_close_a_cycle_is_rejected()
    {
        SeededSpec spec = await SeedAsync();
        SourcedFinding first = FindingsFixture.ParentReview(FindingsFixture.Missing("Errors are not logged.", line: 10));
        SourcedFinding second = FindingsFixture.ParentReview(FindingsFixture.Missing("Retries are missing.", line: 40));
        FindingIssuanceResult initial = await _fixture.IssueAsync(spec.Id, first, second);
        Assert.Single(initial.AddedDependencies);

        FindingIssuanceResult reordered = await _fixture.IssueAsync(spec.Id, second, first);

        Assert.Empty(reordered.AddedDependencies);
        DependencyEdge<TicketRunId> rejected = Assert.Single(reordered.RejectedDependencies);
        Assert.Equal(new DependencyEdge<TicketRunId>(initial.Tickets[0].TicketRunId, initial.Tickets[1].TicketRunId), rejected);
        Assert.Single(_fixture.FindingDependencies(spec.Id));
        Assert.Single(_fixture.Issues.BlockedByCalls);
    }
}
