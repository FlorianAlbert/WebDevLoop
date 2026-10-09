using WebDevLoop.Core.Agents;
using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Events;
using WebDevLoop.Core.Orchestration.Completion.ParentReview;
using WebDevLoop.Core.Orchestration.Completion.Testing;
using WebDevLoop.Core.Orchestration.Integration;
using WebDevLoop.Core.Orchestration.Recovery.AgentSteps;
using WebDevLoop.Core.Orchestration.ReviewLoop;
using WebDevLoop.Core.Orchestration.TicketExecution;
using WebDevLoop.Core.Ports;
using WebDevLoop.Core.Tests.Orchestration.Completion.ParentReview;
using WebDevLoop.Core.Tests.Orchestration.Findings;
using WebDevLoop.Core.Tests.Orchestration.ReviewLoop;
using WebDevLoop.Core.Tests.Orchestration.TicketExecution;

namespace WebDevLoop.Core.Tests.Orchestration.Recovery.AgentSteps;

public sealed class StalledWorkRecoveryTests
{
    private static readonly TicketRunStatus[] ToImplementing = [TicketRunStatus.Ready, TicketRunStatus.Implementing];
    private static readonly TicketRunStatus[] ToReviewing = [.. ToImplementing, TicketRunStatus.Reviewing];
    private static readonly TicketRunStatus[] ToFixing = [.. ToReviewing, TicketRunStatus.FixingReviewFindings];
    private static readonly TicketRunStatus[] ToIntegrating = [.. ToReviewing, TicketRunStatus.Integrating];

    private readonly AgentStepRecoveryFixture _fixture = new();

    [Theory]
    [InlineData(TicketRunStatus.Implementing, RecoveredWorkKind.Implementation)]
    [InlineData(TicketRunStatus.Reviewing, RecoveredWorkKind.ReviewLoop)]
    [InlineData(TicketRunStatus.FixingReviewFindings, RecoveredWorkKind.ReviewLoop)]
    public async Task a_working_ticket_left_without_an_active_step_by_a_previous_process_is_relaunched(TicketRunStatus status, RecoveredWorkKind kind)
    {
        SeededSpec spec = await SeedTicketAsync(PathTo(status));
        _fixture.Restart();

        AgentStepRecoveryReport report = await _fixture.RecoverAsync();

        Assert.Equal([new RecoveredWork(kind, spec.Id, spec[1])], report.Relaunched);
        ImplementationAssignment[] implementations = kind == RecoveredWorkKind.Implementation ? [new(spec.Id, spec[1])] : [];
        ReviewAssignment[] reviews = kind == RecoveredWorkKind.ReviewLoop ? [new(spec.Id, spec[1])] : [];
        Assert.Equal(implementations, _fixture.Execution.Launcher.Launched);
        Assert.Equal(reviews, _fixture.Review.Launcher.Launched);
    }

    [Fact]
    public async Task a_working_ticket_of_this_process_is_relaunched_only_after_the_grace_period()
    {
        SeededSpec spec = await SeedTicketAsync(ToImplementing);
        _fixture.Clock.Advance(AgentStepRecoveryFixture.GracePeriod - TimeSpan.FromSeconds(1));

        AgentStepRecoveryReport early = await _fixture.RecoverAsync();
        _fixture.Clock.Advance(TimeSpan.FromSeconds(1));
        AgentStepRecoveryReport stalled = await _fixture.RecoverAsync();

        Assert.Empty(early.Relaunched);
        Assert.Equal([new RecoveredWork(RecoveredWorkKind.Implementation, spec.Id, spec[1])], stalled.Relaunched);
    }

    [Fact]
    public async Task repeated_interruptions_beyond_max_retries_mark_the_ticket_needs_attention()
    {
        SeededSpec spec = await SeedTicketAsync(ToImplementing);
        _fixture.Execution.Settings.Configure(spec.RepositoryId, settings => settings with { MaxRetries = 1 });
        await _fixture.SeedInterruptedStepAsync(spec.Id, spec[1], StepKind.Implement, AgentRole.Implementer, "impl-1", attempt: 1);
        await _fixture.SeedRunningStepAsync(spec.Id, spec[1], StepKind.Implement, AgentRole.Implementer, "impl-2", attempt: 2);
        _fixture.Restart();

        AgentStepRecoveryReport report = await _fixture.RecoverAsync();

        Assert.Equal(StepInterruption.Restarted, _fixture.Step("impl-2").FailureReason);
        TicketRun ticket = _fixture.Ticket(spec[1]);
        Assert.Equal(TicketRunStatus.NeedsAttention, ticket.Status);
        Assert.Contains("interrupted 2 times in a row", ticket.FailureReason);
        Assert.Equal([spec[1]], report.TicketsNeedingAttention);
        Assert.Empty(report.Relaunched);
        Assert.Contains(
            _fixture.Db.CommittedEvents.OfType<TicketRunStatusChanged>(),
            changed => (changed.TicketRunId, changed.From, changed.To) == (spec[1], TicketRunStatus.Implementing, TicketRunStatus.NeedsAttention));
    }

    [Fact]
    public async Task an_interrupted_fix_resumes_its_session_through_the_review_loop()
    {
        SeededSpec spec = await SeedTicketAsync(ToFixing);
        await _fixture.SeedRunningStepAsync(spec.Id, spec[1], StepKind.Fix, AgentRole.Implementer, "fix-1", session: "session-impl");
        _fixture.Restart();

        AgentStepRecoveryReport report = await _fixture.RecoverAsync();

        Assert.Equal((StepStatus.Failed, StepInterruption.Restarted), (_fixture.Step("fix-1").Status, _fixture.Step("fix-1").FailureReason));
        Assert.Equal([new ReviewAssignment(spec.Id, spec[1], new AgentSessionId("session-impl"))], _fixture.Review.Launcher.Launched);
        Assert.Equal([new RecoveredWork(RecoveredWorkKind.ReviewLoop, spec.Id, spec[1], new AgentSessionId("session-impl"))], report.Relaunched);
    }

    [Fact]
    public async Task interrupted_reviews_of_both_axes_relaunch_the_review_loop_once()
    {
        SeededSpec spec = await SeedTicketAsync(ToReviewing);
        await _fixture.SeedRunningStepAsync(spec.Id, spec[1], StepKind.Review, AgentRole.ReviewerCodingStandards, "review-cs");
        await _fixture.SeedRunningStepAsync(spec.Id, spec[1], StepKind.Review, AgentRole.ReviewerSpecification, "review-spec");
        _fixture.Execution.Settings.Configure(spec.RepositoryId, settings => settings with { MaxRetries = 1 });
        _fixture.Restart();

        AgentStepRecoveryReport report = await _fixture.RecoverAsync();

        Assert.Equal(["review-cs", "review-spec"], report.InterruptedSteps.Select(id => id.Value).Order());
        Assert.Equal(TicketRunStatus.Reviewing, _fixture.Ticket(spec[1]).Status);
        Assert.Equal([new ReviewAssignment(spec.Id, spec[1])], _fixture.Review.Launcher.Launched);
    }

    [Fact]
    public async Task a_parent_review_stuck_without_a_step_is_relaunched()
    {
        SeededSpec spec = await _fixture.Testing.Parent.SeedParentReviewingAsync();
        _fixture.Restart();

        AgentStepRecoveryReport report = await _fixture.RecoverAsync();

        Assert.Equal([new ParentReviewAssignment(spec.Id)], _fixture.Testing.Parent.Launcher.Launched);
        Assert.Equal([new RecoveredWork(RecoveredWorkKind.ParentReview, spec.Id)], report.Relaunched);
    }

    [Fact]
    public async Task a_parent_review_stuck_after_a_github_failure_while_creating_finding_tickets_is_relaunched_and_completes()
    {
        ParentReviewFixture parent = _fixture.Testing.Parent;
        SeededSpec spec = await parent.SeedParentReviewingAsync();
        parent.SpecificationIssues(ReviewLoopFixture.SpecFinding);
        parent.Issues.FailBeforeNextCreate = true;
        await Assert.ThrowsAsync<SimulatedCrashException>(() => parent.RunAsync(spec.Id));
        Assert.Equal(SpecRunStatus.ParentReviewing, parent.Spec(spec.Id).Status);
        int reviewTurns = parent.ReviewerRequests.Count();

        AgentStepRecoveryReport early = await _fixture.RecoverAsync();
        _fixture.Clock.Advance(AgentStepRecoveryFixture.GracePeriod);
        AgentStepRecoveryReport stalled = await _fixture.RecoverAsync();
        ParentReviewResult result = await parent.Runner().RunAsync(parent.Launcher.Launched[^1], AgentStepRecoveryFixture.Token);

        Assert.Empty(early.Relaunched);
        Assert.Equal([new RecoveredWork(RecoveredWorkKind.ParentReview, spec.Id)], stalled.Relaunched);
        Assert.Equal(ParentReviewOutcome.FindingTicketsCreated, result.Outcome);
        Assert.Equal(reviewTurns, parent.ReviewerRequests.Count());
        Assert.Single(parent.Issues.CreatedDrafts);
        Assert.Equal(SpecRunStatus.Running, parent.Spec(spec.Id).Status);
    }

    [Fact]
    public async Task an_interrupted_parent_review_step_is_finished_and_the_review_relaunched()
    {
        SeededSpec spec = await _fixture.Testing.Parent.SeedParentReviewingAsync();
        await _fixture.SeedRunningStepAsync(spec.Id, null, StepKind.ParentReview, AgentRole.ReviewerSpecification, "parent-spec");
        _fixture.Restart();

        await _fixture.RecoverAsync();

        Assert.Equal(StepStatus.Failed, _fixture.Step("parent-spec").Status);
        Assert.Equal([new ParentReviewAssignment(spec.Id)], _fixture.Testing.Parent.Launcher.Launched);
    }

    [Fact]
    public async Task a_testing_spec_without_a_test_step_is_relaunched()
    {
        SeededSpec spec = await _fixture.Testing.SeedTestingAsync();
        _fixture.Restart();

        AgentStepRecoveryReport report = await _fixture.RecoverAsync();

        Assert.Equal([new TestingAssignment(spec.Id)], _fixture.Testing.Launcher.Launched);
        Assert.Equal([new RecoveredWork(RecoveredWorkKind.Testing, spec.Id)], report.Relaunched);
    }

    [Fact]
    public async Task an_interrupted_conflict_resolution_relaunches_the_integration_saga()
    {
        SeededSpec spec = await SeedTicketAsync(ToIntegrating);
        await _fixture.SeedRunningStepAsync(spec.Id, spec[1], StepKind.ResolveConflict, AgentRole.ConflictResolver, "resolve-1");
        _fixture.Restart();

        AgentStepRecoveryReport report = await _fixture.RecoverAsync();

        Assert.Equal(StepStatus.Failed, _fixture.Step("resolve-1").Status);
        Assert.Equal([new IntegrationAssignment(spec.RepositoryId, spec.Id, spec[1])], _fixture.Integration.Launched);
        Assert.Equal([new RecoveredWork(RecoveredWorkKind.Integration, spec.Id, spec[1])], report.Relaunched);
    }

    [Fact]
    public async Task an_integrating_ticket_without_an_interrupted_step_is_left_to_saga_reconciliation()
    {
        await SeedTicketAsync(ToIntegrating);
        _fixture.Restart();

        AgentStepRecoveryReport report = await _fixture.RecoverAsync();

        Assert.Empty(report.Relaunched);
        Assert.Empty(_fixture.Integration.Launched);
    }

    [Fact]
    public async Task an_interrupted_exploration_reports_the_spec_for_preparation()
    {
        RunId preparing = await SeedPreparingSpecAsync();
        await _fixture.SeedRunningStepAsync(preparing, null, StepKind.Explore, AgentRole.Explorer, "explore-1");
        _fixture.Restart();

        AgentStepRecoveryReport report = await _fixture.RecoverAsync();

        Assert.Equal(StepStatus.Failed, _fixture.Step("explore-1").Status);
        Assert.Equal([preparing], report.SpecsAwaitingPreparation);
        Assert.Empty(report.Relaunched);
    }

    [Fact]
    public async Task an_interrupted_step_of_an_aborted_ticket_is_finished_without_relaunching()
    {
        SeededSpec spec = await SeedTicketAsync([.. ToImplementing, TicketRunStatus.Aborted]);
        await _fixture.SeedRunningStepAsync(spec.Id, spec[1], StepKind.Implement, AgentRole.Implementer, "impl-1");
        _fixture.Restart();

        AgentStepRecoveryReport report = await _fixture.RecoverAsync();

        Assert.Equal(StepInterruption.Restarted, _fixture.Step("impl-1").FailureReason);
        Assert.Equal(TicketRunStatus.Aborted, _fixture.Ticket(spec[1]).Status);
        Assert.Empty(report.Relaunched);
        Assert.Empty(_fixture.Execution.Launcher.Launched);
    }

    [Fact]
    public async Task a_failing_runtime_refresh_is_reported_and_recovery_continues()
    {
        SeededSpec spec = await _fixture.Testing.SeedTestingAsync();
        _fixture.Runtimes.RefreshFailure = new InvalidOperationException("Token service unavailable.");
        _fixture.Restart();

        AgentStepRecoveryReport report = await _fixture.RecoverAsync();

        Assert.Contains("Token service unavailable.", report.RuntimeMaintenanceFailure);
        Assert.Equal([new TestingAssignment(spec.Id)], _fixture.Testing.Launcher.Launched);
    }

    private static TicketRunStatus[] PathTo(TicketRunStatus status) => status switch
    {
        TicketRunStatus.Implementing => ToImplementing,
        TicketRunStatus.Reviewing => ToReviewing,
        _ => ToFixing,
    };

    private async Task<SeededSpec> SeedTicketAsync(TicketRunStatus[] path)
    {
        SeededSpec spec = await _fixture.Execution.SeedRunningSpecAsync("app", (1, []));
        await _fixture.Execution.MoveAsync(spec[1], path);
        return spec;
    }

    private async Task<RunId> SeedPreparingSpecAsync()
    {
        var id = new RunId("run-preparing");
        SpecRun spec = SpecRun.Queue(id, 1, new IssueRef("octo", "app", 9), "Spec", "spec body", 1, _fixture.Clock.UtcNow);
        spec.TransitionTo(SpecRunStatus.Preparing, _fixture.Clock.UtcNow);
        CasWorkflowScope scope = _fixture.Db.OpenScope();
        scope.Add(spec);
        Assert.Equal(SaveOutcome.Saved, await scope.SaveChangesAsync(AgentStepRecoveryFixture.Token));
        return id;
    }
}
