using WebDevLoop.Core.Agents;
using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Events;
using WebDevLoop.Core.Orchestration.Completion.ParentReview;
using WebDevLoop.Core.Orchestration.Findings;
using WebDevLoop.Core.Orchestration.Frontier;
using WebDevLoop.Core.Orchestration.ReviewLoop;
using WebDevLoop.Core.Orchestration.Results;
using WebDevLoop.Core.Ports;
using WebDevLoop.Core.Settings;
using WebDevLoop.Core.Tests.Orchestration.Findings;
using WebDevLoop.Core.Tests.Orchestration.ReviewLoop;
using WebDevLoop.Core.Tests.Orchestration.TicketExecution;

namespace WebDevLoop.Core.Tests.Orchestration.Completion.ParentReview;

/// <summary>
/// Runs the parent-spec review with the real WP-15 two-axis review runner and the real finding issuer against scripted
/// reviewers, in-memory Git, and an in-memory issue tracker. Every service call gets a fresh unit-of-work scope.
/// </summary>
internal sealed class ParentReviewFixture
{
    private int _eventMark;

    public ReviewLoopFixture Review { get; } = new();

    public FindingIssuanceStore Issuances { get; } = new();

    public FindingIssueTracker Issues { get; } = new();

    public TestParentReviewLauncher Launcher { get; } = new();

    public TicketExecutionFixture Execution => Review.Execution;

    public CasWorkflowDatabase Db => Review.Db;

    public static CancellationToken Token => TicketExecutionFixture.Token;

    public FindingWorkflowScope OpenScope() => new(Db.OpenScope(), Issuances);

    public ParentSpecReviewRunner Runner(FindingWorkflowScope? scope = null)
    {
        scope ??= OpenScope();
        CasWorkflowScope workflow = scope.Workflow;
        var issuer = new FindingTicketIssuer(workflow, scope, Issues, scope, Execution.Ids, Execution.Clock);
        return new ParentSpecReviewRunner(
            workflow, workflow, workflow, workflow, Review.Settings, Review.Git, Review.Reviews(workflow), issuer, workflow, scope, Execution.Clock);
    }

    public ParentReviewStarter Starter()
    {
        CasWorkflowScope scope = Db.OpenScope();
        return new ParentReviewStarter(scope, scope, scope, scope, Execution.Clock);
    }

    public ParentReviewEventHandler Handler() => new(Starter(), Launcher);

    /// <summary>
    /// Outbox dispatcher: delivers committed events to the frontier (WP-14) and parent-review handlers until no new events
    /// appear; launched parent reviews are only recorded.
    /// </summary>
    public async Task PumpEventsAsync()
    {
        const int MaxRounds = 50;
        long messageId = 0;
        for (int round = 0; round < MaxRounds; round++)
        {
            IReadOnlyList<WorkflowEvent> pending = Db.TakeUndispatchedEvents();
            if (pending.Count == 0)
            {
                return;
            }

            foreach (WorkflowEvent workflowEvent in pending)
            {
                var envelope = new EventEnvelope(++messageId, workflowEvent);
                await new FrontierEventHandler(Execution.Frontier()).HandleAsync(envelope, Token);
                await Handler().HandleAsync(envelope, Token);
            }
        }

        throw new InvalidOperationException("Event pumping did not settle.");
    }

    public Task<ParentReviewResult> RunAsync(RunId specRunId) => Runner().RunAsync(new ParentReviewAssignment(specRunId), Token);

    /// <summary>A running spec whose single ticket (#2) is implemented and integrated, so its integration branch has changes.</summary>
    public async Task<SeededSpec> SeedIntegratedSpecAsync()
    {
        SeededSpec spec = await Execution.SeedRunningSpecAsync("app", (2, []));
        await Execution.MoveAsync(spec[2], TicketRunStatus.Ready, TicketRunStatus.Implementing);
        await Execution.IntegrateAsync(spec, spec[2]);
        return spec;
    }

    public async Task<SeededSpec> SeedParentReviewingAsync()
    {
        SeededSpec spec = await SeedIntegratedSpecAsync();
        await MoveSpecAsync(spec.Id, SpecRunStatus.ParentReviewing);
        Db.TakeUndispatchedEvents();
        _eventMark = Db.CommittedEvents.Count;
        return spec;
    }

    public async Task MoveSpecAsync(RunId specRunId, SpecRunStatus next)
    {
        CasWorkflowScope scope = Db.OpenScope();
        SpecRun spec = (await scope.GetAsync(specRunId, Token))!;
        SpecRunStatus previous = spec.Status;
        spec.TransitionTo(next, Execution.Clock.UtcNow);
        scope.Append(new SpecRunStatusChanged(spec.Id, spec.RepositoryId, previous, next, Execution.Clock.UtcNow));
        Assert.Equal(SaveOutcome.Saved, await scope.SaveChangesAsync(Token));
    }

    /// <summary>Simulates the normal ticket flow for a finding ticket: implemented, reviewed, squash-merged, integrated.</summary>
    public async Task WorkTicketAsync(SeededSpec spec, TicketRunId ticketId)
    {
        await Execution.MoveAsync(ticketId, TicketRunStatus.Ready, TicketRunStatus.Implementing);
        await Execution.IntegrateAsync(spec, ticketId);
    }

    public void UseReviewerTemplate(string template)
    {
        Review.UseTemplate(AgentRole.ReviewerCodingStandards, template);
        Review.UseTemplate(AgentRole.ReviewerSpecification, template);
    }

    public ParentReviewFixture Clean()
    {
        Review.Clean(FindingAxis.CodingStandards).Clean(FindingAxis.Specification);
        return this;
    }

    public ParentReviewFixture SpecificationIssues(params Finding[] findings)
    {
        Review.Clean(FindingAxis.CodingStandards).Issues(FindingAxis.Specification, findings);
        return this;
    }

    public SpecRun Spec(RunId id) => Execution.Spec(id);

    public IReadOnlyList<TicketRun> Tickets(RunId specRunId) =>
        Db.Rows<TicketRun>().Where(ticket => ticket.SpecRunId == specRunId).OrderBy(ticket => ticket.Issue.Number).ToArray();

    public IReadOnlyList<StepRun> ParentReviewSteps(RunId specRunId) =>
        Db.Rows<StepRun>().Where(step => step.SpecRunId == specRunId && step.Kind == StepKind.ParentReview).OrderBy(step => step.Id.Value).ToArray();

    public IEnumerable<AgentRunRequest> ReviewerRequests => Review.ReviewerRequests;

    /// <summary>Spec transitions committed after seeding.</summary>
    public IEnumerable<SpecRunStatusChanged> SpecTransitions(RunId specRunId) =>
        Db.CommittedEvents.Skip(_eventMark).OfType<SpecRunStatusChanged>().Where(changed => changed.SpecRunId == specRunId);
}

internal sealed class TestParentReviewLauncher : IParentReviewLauncher
{
    public List<ParentReviewAssignment> Launched { get; } = [];

    public void Launch(ParentReviewAssignment assignment) => Launched.Add(assignment);
}
