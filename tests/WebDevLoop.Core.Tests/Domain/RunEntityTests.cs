using WebDevLoop.Core.Domain;

namespace WebDevLoop.Core.Tests.Domain;

public sealed class RunEntityTests
{
    private static readonly DateTimeOffset T0 = new(2026, 1, 1, 10, 0, 0, TimeSpan.Zero);
    private static readonly IssueRef SpecIssue = new("o", "r", 5);

    private static SpecRun NewSpec(string runId = "run1") =>
        SpecRun.Queue(new RunId(runId), 1, SpecIssue, "Spec", "body", 1, T0);

    private static TicketRun NewTicket(string runId = "run1", string ticketId = "t1") =>
        TicketRun.Create(new TicketRunId(ticketId), new RunId(runId), new IssueRef("o", "r", 6), "Ticket", "body", T0);

    [Fact]
    public void queued_spec_starts_with_run_scoped_integration_branch_and_version_zero()
    {
        SpecRun spec = NewSpec();

        Assert.Equal(SpecRunStatus.Queued, spec.Status);
        Assert.Equal("webdevloop/run1/integration", spec.IntegrationBranch.Value);
        Assert.Equal(0, spec.Version);
        Assert.Equal(T0, spec.CreatedAt);
    }

    [Fact]
    public void repeated_spec_runs_for_the_same_issue_get_distinct_integration_branches()
    {
        Assert.NotEqual(NewSpec("run1").IntegrationBranch, NewSpec("run2").IntegrationBranch);
        Assert.Equal(NewSpec("run1").ParentIssue, NewSpec("run2").ParentIssue);
    }

    [Fact]
    public void spec_walks_the_full_lifecycle_and_records_timestamps()
    {
        SpecRun spec = NewSpec();
        DateTimeOffset at = T0;

        foreach (SpecRunStatus next in new[]
        {
            SpecRunStatus.Preparing, SpecRunStatus.Running, SpecRunStatus.ParentReviewing, SpecRunStatus.Testing,
            SpecRunStatus.ReadyForReview, SpecRunStatus.AwaitingMerge, SpecRunStatus.Completed,
        })
        {
            at = at.AddMinutes(1);
            spec.TransitionTo(next, at);
            Assert.Equal(next, spec.Status);
        }

        Assert.Equal(T0.AddMinutes(1), spec.StartedAt);
        Assert.Equal(T0.AddMinutes(5), spec.ReadyAt);
        Assert.Equal(T0.AddMinutes(7), spec.CompletedAt);
        Assert.True(spec.IsTerminal);
    }

    [Fact]
    public void ready_for_review_spec_is_active_and_not_terminal()
    {
        SpecRun spec = NewSpec();
        foreach (SpecRunStatus next in new[]
        {
            SpecRunStatus.Preparing, SpecRunStatus.Running, SpecRunStatus.ParentReviewing, SpecRunStatus.Testing,
            SpecRunStatus.ReadyForReview,
        })
        {
            spec.TransitionTo(next, T0);
        }

        Assert.True(spec.IsActive);
        Assert.False(spec.IsTerminal);
        Assert.Null(spec.CompletedAt);
    }

    [Fact]
    public void illegal_spec_transition_throws_and_leaves_state_unchanged()
    {
        SpecRun spec = NewSpec();

        var error = Assert.Throws<InvalidStatusTransitionException>(() => spec.TransitionTo(SpecRunStatus.Testing, T0));

        Assert.Equal(SpecRunStatus.Queued, spec.Status);
        Assert.Equal(SpecRunStatus.Queued, error.From);
        Assert.Equal(SpecRunStatus.Testing, error.To);
    }

    [Fact]
    public void spec_counts_review_and_test_cycles_on_entry()
    {
        SpecRun spec = NewSpec();
        spec.TransitionTo(SpecRunStatus.Preparing, T0);
        spec.TransitionTo(SpecRunStatus.Running, T0);
        spec.TransitionTo(SpecRunStatus.ParentReviewing, T0);
        spec.TransitionTo(SpecRunStatus.Running, T0);
        spec.TransitionTo(SpecRunStatus.ParentReviewing, T0);
        spec.TransitionTo(SpecRunStatus.Testing, T0);

        Assert.Equal(2, spec.ReviewCycle);
        Assert.Equal(1, spec.TestCycle);
    }

    [Fact]
    public void spec_needs_attention_records_reason_and_retry_clears_it()
    {
        SpecRun spec = NewSpec();
        spec.TransitionTo(SpecRunStatus.Preparing, T0);

        spec.MarkNeedsAttention("review cycle limit reached", T0);

        Assert.Equal(SpecRunStatus.NeedsAttention, spec.Status);
        Assert.Equal("review cycle limit reached", spec.FailureReason);

        spec.TransitionTo(SpecRunStatus.Preparing, T0);

        Assert.Null(spec.FailureReason);
    }

    [Fact]
    public void completed_spec_cannot_need_attention()
    {
        SpecRun spec = NewSpec();
        spec.TransitionTo(SpecRunStatus.Aborted, T0);

        Assert.Throws<InvalidStatusTransitionException>(() => spec.MarkNeedsAttention("late", T0));
    }

    [Fact]
    public void version_starts_at_zero_and_advances_by_one()
    {
        SpecRun spec = NewSpec();
        spec.AdvanceVersion();
        spec.AdvanceVersion();

        Assert.Equal(2, spec.Version);
    }

    [Fact]
    public void ticket_starts_blocked_with_run_scoped_branch()
    {
        TicketRun ticket = NewTicket();

        Assert.Equal(TicketRunStatus.Blocked, ticket.Status);
        Assert.Equal("webdevloop/run1/ticket/t1", ticket.BranchName.Value);
        Assert.Equal(0, ticket.Version);
    }

    [Fact]
    public void ticket_cannot_go_from_blocked_directly_to_integrating()
    {
        TicketRun ticket = NewTicket();

        Assert.Throws<InvalidStatusTransitionException>(() => ticket.TransitionTo(TicketRunStatus.Integrating, T0));
        Assert.Equal(TicketRunStatus.Blocked, ticket.Status);
    }

    [Fact]
    public void ticket_attempt_and_review_iteration_are_counted_on_entry()
    {
        TicketRun ticket = NewTicket();
        ticket.TransitionTo(TicketRunStatus.Ready, T0);
        ticket.TransitionTo(TicketRunStatus.Implementing, T0);
        ticket.TransitionTo(TicketRunStatus.Reviewing, T0);
        ticket.TransitionTo(TicketRunStatus.FixingReviewFindings, T0);
        ticket.TransitionTo(TicketRunStatus.Reviewing, T0);
        ticket.TransitionTo(TicketRunStatus.FixingReviewFindings, T0);

        Assert.Equal(1, ticket.Attempt);
        Assert.Equal(2, ticket.ReviewIteration);

        ticket.MarkNeedsAttention("limit", T0);
        ticket.TransitionTo(TicketRunStatus.Ready, T0);
        ticket.TransitionTo(TicketRunStatus.Implementing, T0);

        Assert.Equal(2, ticket.Attempt);
        Assert.Equal(0, ticket.ReviewIteration);
        Assert.Null(ticket.FailureReason);
    }

    [Fact]
    public void ticket_updated_at_follows_transitions()
    {
        TicketRun ticket = NewTicket();

        ticket.TransitionTo(TicketRunStatus.Ready, T0.AddHours(1));

        Assert.Equal(T0.AddHours(1), ticket.UpdatedAt);
    }

    [Fact]
    public void ticket_needs_attention_keeps_reason()
    {
        TicketRun ticket = NewTicket();

        ticket.MarkNeedsAttention("max retries", T0);

        Assert.Equal(TicketRunStatus.NeedsAttention, ticket.Status);
        Assert.Equal("max retries", ticket.FailureReason);
    }

    [Fact]
    public void integrated_ticket_is_terminal()
    {
        TicketRun ticket = NewTicket();
        foreach (TicketRunStatus next in new[]
        {
            TicketRunStatus.Ready, TicketRunStatus.Implementing, TicketRunStatus.Reviewing,
            TicketRunStatus.Integrating, TicketRunStatus.Integrated,
        })
        {
            ticket.TransitionTo(next, T0);
        }

        Assert.True(ticket.IsTerminal);
        Assert.Throws<InvalidStatusTransitionException>(() => ticket.TransitionTo(TicketRunStatus.Ready, T0));
    }

    [Fact]
    public void step_lifecycle_records_start_timeout_and_outcome()
    {
        StepRun step = StepRun.Create(new StepRunId("s1"), new RunId("run1"), new TicketRunId("t1"), StepKind.Implement, AgentRole.Implementer, 1, "hash");

        step.Start(T0, TimeSpan.FromMinutes(30));

        Assert.Equal(StepStatus.Running, step.Status);
        Assert.Equal(T0, step.StartedAt);
        Assert.Equal(T0.AddMinutes(30), step.TimeoutAt);
        Assert.True(step.IsActive);

        step.Finish(StepStatus.Succeeded, T0.AddMinutes(5), "{\"ok\":true}");

        Assert.Equal(StepStatus.Succeeded, step.Status);
        Assert.Equal(T0.AddMinutes(5), step.CompletedAt);
        Assert.Equal("{\"ok\":true}", step.StructuredResultJson);
        Assert.False(step.IsActive);
    }

    [Fact]
    public void step_failure_keeps_reason()
    {
        StepRun step = StepRun.Create(new StepRunId("s1"), new RunId("run1"), null, StepKind.ParentReview, null, 1, "hash");
        step.Start(T0, TimeSpan.FromMinutes(1));

        step.Finish(StepStatus.TimedOut, T0.AddMinutes(1), failureReason: "timed out");

        Assert.Equal(StepStatus.TimedOut, step.Status);
        Assert.Equal("timed out", step.FailureReason);
    }

    [Fact]
    public void pending_step_cannot_finish_successfully_without_starting()
    {
        StepRun step = StepRun.Create(new StepRunId("s1"), new RunId("run1"), null, StepKind.Explore, AgentRole.Explorer, 1, "hash");

        Assert.Throws<InvalidStatusTransitionException>(() => step.Finish(StepStatus.Succeeded, T0));
        Assert.Throws<InvalidStatusTransitionException>(() => { step.Start(T0, TimeSpan.FromMinutes(1)); step.Start(T0, TimeSpan.FromMinutes(1)); });
    }

    [Theory]
    [InlineData(StepKind.Implement, true)]
    [InlineData(StepKind.Fix, true)]
    [InlineData(StepKind.Review, false)]
    [InlineData(StepKind.Test, false)]
    public void only_implement_and_fix_steps_share_the_ticket_exclusive_slot(StepKind kind, bool expected)
    {
        Assert.Equal(expected, kind.IsImplementOrFix());
    }
}
