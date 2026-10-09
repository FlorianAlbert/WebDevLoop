using WebDevLoop.Core.Domain;

namespace WebDevLoop.Core.Tests.Domain;

/// <summary>Domain rules behind the user's Retry/Skip/Abort control actions.</summary>
public sealed class RunControlRulesTests
{
    private static readonly DateTimeOffset T0 = new(2026, 1, 1, 10, 0, 0, TimeSpan.Zero);
    private static readonly CommitSha Head = new("1111111111111111111111111111111111111111");

    private static SpecRun NewSpec() => SpecRun.Queue(new RunId("run1"), 1, new IssueRef("o", "r", 5), "Spec", "body", 1, T0);

    private static TicketRun NewTicket() =>
        TicketRun.Create(new TicketRunId("t1"), new RunId("run1"), new IssueRef("o", "r", 6), "Ticket", "body", T0);

    private static TicketRun TicketIn(params TicketRunStatus[] path)
    {
        TicketRun ticket = NewTicket();
        foreach (TicketRunStatus next in path)
        {
            ticket.TransitionTo(next, T0);
        }

        return ticket;
    }

    [Fact]
    public void a_spec_needing_attention_remembers_the_phase_it_failed_in()
    {
        SpecRun spec = NewSpec();
        spec.TransitionTo(SpecRunStatus.Preparing, T0);
        spec.TransitionTo(SpecRunStatus.Running, T0);
        spec.TransitionTo(SpecRunStatus.ParentReviewing, T0);

        spec.MarkNeedsAttention("review failed", T0);

        Assert.Equal(SpecRunStatus.ParentReviewing, spec.NeedsAttentionFrom);
        Assert.Equal("review failed", spec.FailureReason);
    }

    [Fact]
    public void leaving_needs_attention_forgets_the_failed_phase()
    {
        SpecRun spec = NewSpec();
        spec.TransitionTo(SpecRunStatus.Preparing, T0);
        spec.MarkNeedsAttention("clone failed", T0);

        spec.TransitionTo(SpecRunStatus.Preparing, T0);

        Assert.Null(spec.NeedsAttentionFrom);
    }

    [Theory]
    [InlineData(SpecRunStatus.NeedsAttention)]
    [InlineData(SpecRunStatus.Aborted)]
    [InlineData(SpecRunStatus.AwaitingMerge)]
    public void a_spec_leaving_the_active_phases_releases_its_active_slot(SpecRunStatus parked)
    {
        SpecRun spec = NewSpec();
        spec.MaxActiveSpecsSlot = 1;
        spec.TransitionTo(SpecRunStatus.Preparing, T0);
        spec.TransitionTo(SpecRunStatus.Running, T0);
        spec.TransitionTo(SpecRunStatus.ParentReviewing, T0);
        spec.TransitionTo(SpecRunStatus.Testing, T0);
        if (parked == SpecRunStatus.AwaitingMerge)
        {
            spec.TransitionTo(SpecRunStatus.ReadyForReview, T0);
        }

        spec.TransitionTo(parked, T0);

        Assert.Null(spec.MaxActiveSpecsSlot);
    }

    [Fact]
    public void an_active_spec_keeps_its_slot_between_active_phases()
    {
        SpecRun spec = NewSpec();
        spec.MaxActiveSpecsSlot = 2;
        spec.TransitionTo(SpecRunStatus.Preparing, T0);
        spec.TransitionTo(SpecRunStatus.Running, T0);

        Assert.Equal(2, spec.MaxActiveSpecsSlot);
    }

    [Fact]
    public void a_spec_needing_attention_can_resume_merge_tracking_from_ready_for_review()
    {
        Assert.True(SpecRunStatus.NeedsAttention.CanTransitionTo(SpecRunStatus.ReadyForReview));
    }

    [Fact]
    public void a_ticket_needing_attention_remembers_the_phase_it_failed_in()
    {
        TicketRun ticket = TicketIn(TicketRunStatus.Ready, TicketRunStatus.Implementing, TicketRunStatus.Reviewing, TicketRunStatus.Integrating);

        ticket.MarkNeedsAttention("push rejected", T0);

        Assert.Equal(TicketRunStatus.Integrating, ticket.NeedsAttentionFrom);
    }

    [Fact]
    public void retrying_a_ticket_that_failed_while_implementing_reimplements_it()
    {
        TicketRun ticket = TicketIn(TicketRunStatus.Ready, TicketRunStatus.Implementing);
        ticket.MarkNeedsAttention("retries exhausted", T0);

        TicketRunStatus target = ticket.Retry(T0);

        Assert.Equal((TicketRunStatus.Ready, TicketRunStatus.Ready), (target, ticket.Status));
        Assert.Null(ticket.FailureReason);
        Assert.Null(ticket.NeedsAttentionFrom);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void retrying_a_ticket_that_failed_in_review_starts_a_fresh_review_round(bool failedDuringFix)
    {
        TicketRun ticket = TicketIn(TicketRunStatus.Ready, TicketRunStatus.Implementing, TicketRunStatus.Reviewing, TicketRunStatus.FixingReviewFindings, TicketRunStatus.Reviewing);
        if (failedDuringFix)
        {
            ticket.TransitionTo(TicketRunStatus.FixingReviewFindings, T0);
        }

        ticket.LastImplementedSha = Head;
        ticket.MarkNeedsAttention("review iterations exhausted", T0);

        TicketRunStatus target = ticket.Retry(T0);

        Assert.Equal(TicketRunStatus.Reviewing, target);
        Assert.Equal((2, 0), (ticket.Attempt, ticket.ReviewIteration));
    }

    [Fact]
    public void retrying_a_ticket_that_failed_while_integrating_resumes_its_integration()
    {
        TicketRun ticket = TicketIn(TicketRunStatus.Ready, TicketRunStatus.Implementing, TicketRunStatus.Reviewing, TicketRunStatus.Integrating);
        ticket.LastImplementedSha = Head;
        ticket.MarkNeedsAttention("diff verification failed", T0);

        Assert.Equal(TicketRunStatus.Integrating, ticket.Retry(T0));
        Assert.Equal((1, 0), (ticket.Attempt, ticket.ReviewIteration));
    }

    [Theory]
    [InlineData(TicketRunStatus.Implementing)]
    [InlineData(TicketRunStatus.Reviewing)]
    public void a_ticket_whose_integration_already_squashed_resumes_the_integration_whatever_phase_failed(TicketRunStatus failedIn)
    {
        TicketRun ticket = TicketIn(TicketRunStatus.Ready, TicketRunStatus.Implementing);
        if (failedIn == TicketRunStatus.Reviewing)
        {
            ticket.TransitionTo(TicketRunStatus.Reviewing, T0);
        }

        ticket.MarkNeedsAttention("interrupted", T0);

        Assert.Equal(TicketRunStatus.Integrating, ticket.Retry(T0, integrationInProgress: true));
        Assert.Equal((1, 0), (ticket.Attempt, ticket.ReviewIteration));
    }

    [Fact]
    public void a_ticket_without_an_implemented_commit_is_reimplemented_whatever_phase_failed()
    {
        TicketRun ticket = TicketIn(TicketRunStatus.Ready, TicketRunStatus.Implementing, TicketRunStatus.Reviewing);
        ticket.MarkNeedsAttention("implemented commit missing", T0);

        Assert.Equal(TicketRunStatus.Ready, ticket.Retry(T0));
    }

    [Fact]
    public void only_a_ticket_needing_attention_can_be_retried()
    {
        TicketRun ticket = TicketIn(TicketRunStatus.Ready, TicketRunStatus.Implementing);

        Assert.Throws<InvalidStatusTransitionException>(() => ticket.Retry(T0));
        Assert.Equal(TicketRunStatus.Implementing, ticket.Status);
    }

    [Theory]
    [InlineData(TicketRunStatus.Integrated, true)]
    [InlineData(TicketRunStatus.Skipped, true)]
    [InlineData(TicketRunStatus.Aborted, false)]
    [InlineData(TicketRunStatus.NeedsAttention, false)]
    [InlineData(TicketRunStatus.Integrating, false)]
    public void integrated_and_skipped_blockers_satisfy_their_dependents(TicketRunStatus blocker, bool satisfies)
    {
        Assert.Equal(satisfies, blocker.SatisfiesDependents());
    }
}
