using WebDevLoop.Core.Domain;

namespace WebDevLoop.Core.Tests.Domain;

public sealed class StatusRulesTests
{
    private static readonly SpecRunStatus[] HappyPath =
    [
        SpecRunStatus.Queued,
        SpecRunStatus.Preparing,
        SpecRunStatus.Running,
        SpecRunStatus.ParentReviewing,
        SpecRunStatus.Testing,
        SpecRunStatus.ReadyForReview,
        SpecRunStatus.AwaitingMerge,
        SpecRunStatus.Completed,
    ];

    [Fact]
    public void spec_transitions_through_the_happy_path_in_order()
    {
        for (int i = 0; i < HappyPath.Length - 1; i++)
        {
            Assert.True(HappyPath[i].CanTransitionTo(HappyPath[i + 1]), $"{HappyPath[i]} -> {HappyPath[i + 1]}");
        }
    }

    [Fact]
    public void spec_can_wait_for_dependency_before_preparing()
    {
        Assert.True(SpecRunStatus.Queued.CanTransitionTo(SpecRunStatus.WaitingForDependency));
        Assert.True(SpecRunStatus.WaitingForDependency.CanTransitionTo(SpecRunStatus.Preparing));
    }

    [Fact]
    public void spec_findings_loop_back_to_running()
    {
        Assert.True(SpecRunStatus.ParentReviewing.CanTransitionTo(SpecRunStatus.Running));
        Assert.True(SpecRunStatus.Testing.CanTransitionTo(SpecRunStatus.Running));
    }

    [Theory]
    [InlineData(SpecRunStatus.Queued, SpecRunStatus.Running)]
    [InlineData(SpecRunStatus.Running, SpecRunStatus.Testing)]
    [InlineData(SpecRunStatus.ReadyForReview, SpecRunStatus.Completed)]
    [InlineData(SpecRunStatus.AwaitingMerge, SpecRunStatus.Running)]
    [InlineData(SpecRunStatus.Completed, SpecRunStatus.Running)]
    [InlineData(SpecRunStatus.Aborted, SpecRunStatus.Queued)]
    public void spec_rejects_skipping_or_reviving_transitions(SpecRunStatus from, SpecRunStatus to)
    {
        Assert.False(from.CanTransitionTo(to));
    }

    [Fact]
    public void spec_non_terminal_states_can_need_attention_or_abort()
    {
        foreach (SpecRunStatus status in Enum.GetValues<SpecRunStatus>().Where(s => !s.IsTerminal()))
        {
            if (status != SpecRunStatus.NeedsAttention)
            {
                Assert.True(status.CanTransitionTo(SpecRunStatus.NeedsAttention), $"{status} -> NeedsAttention");
            }

            Assert.True(status.CanTransitionTo(SpecRunStatus.Aborted), $"{status} -> Aborted");
        }
    }

    [Fact]
    public void spec_terminal_states_have_no_outgoing_transitions()
    {
        foreach (SpecRunStatus terminal in new[] { SpecRunStatus.Completed, SpecRunStatus.Aborted })
        {
            Assert.All(Enum.GetValues<SpecRunStatus>(), to => Assert.False(terminal.CanTransitionTo(to)));
        }
    }

    [Fact]
    public void ready_for_review_and_awaiting_merge_are_active_not_terminal()
    {
        foreach (SpecRunStatus status in new[] { SpecRunStatus.ReadyForReview, SpecRunStatus.AwaitingMerge })
        {
            Assert.True(status.IsActive());
            Assert.False(status.IsTerminal());
        }
    }

    [Theory]
    [InlineData(SpecRunStatus.Completed)]
    [InlineData(SpecRunStatus.Aborted)]
    public void completed_and_aborted_are_terminal_and_inactive(SpecRunStatus status)
    {
        Assert.True(status.IsTerminal());
        Assert.False(status.IsActive());
    }

    [Theory]
    [InlineData(SpecRunStatus.Queued)]
    [InlineData(SpecRunStatus.WaitingForDependency)]
    [InlineData(SpecRunStatus.NeedsAttention)]
    public void waiting_states_are_neither_terminal_nor_active(SpecRunStatus status)
    {
        Assert.False(status.IsTerminal());
        Assert.False(status.IsActive());
    }

    [Fact]
    public void ticket_follows_the_happy_path_with_a_fix_loop()
    {
        Assert.True(TicketRunStatus.Blocked.CanTransitionTo(TicketRunStatus.Ready));
        Assert.True(TicketRunStatus.Ready.CanTransitionTo(TicketRunStatus.Implementing));
        Assert.True(TicketRunStatus.Implementing.CanTransitionTo(TicketRunStatus.Reviewing));
        Assert.True(TicketRunStatus.Reviewing.CanTransitionTo(TicketRunStatus.FixingReviewFindings));
        Assert.True(TicketRunStatus.FixingReviewFindings.CanTransitionTo(TicketRunStatus.Reviewing));
        Assert.True(TicketRunStatus.Reviewing.CanTransitionTo(TicketRunStatus.Integrating));
        Assert.True(TicketRunStatus.Integrating.CanTransitionTo(TicketRunStatus.Integrated));
    }

    [Theory]
    [InlineData(TicketRunStatus.Blocked, TicketRunStatus.Integrating)]
    [InlineData(TicketRunStatus.Blocked, TicketRunStatus.Implementing)]
    [InlineData(TicketRunStatus.Ready, TicketRunStatus.Integrating)]
    [InlineData(TicketRunStatus.Implementing, TicketRunStatus.Integrating)]
    [InlineData(TicketRunStatus.Integrated, TicketRunStatus.Ready)]
    public void ticket_rejects_illegal_transitions(TicketRunStatus from, TicketRunStatus to)
    {
        Assert.False(from.CanTransitionTo(to));
    }

    [Fact]
    public void ticket_needs_attention_can_be_retried_skipped_or_aborted()
    {
        Assert.True(TicketRunStatus.NeedsAttention.CanTransitionTo(TicketRunStatus.Ready));
        Assert.True(TicketRunStatus.NeedsAttention.CanTransitionTo(TicketRunStatus.Skipped));
        Assert.True(TicketRunStatus.NeedsAttention.CanTransitionTo(TicketRunStatus.Aborted));
        Assert.False(TicketRunStatus.NeedsAttention.IsTerminal());
    }

    [Fact]
    public void ticket_terminal_states_have_no_outgoing_transitions()
    {
        foreach (TicketRunStatus terminal in new[] { TicketRunStatus.Integrated, TicketRunStatus.Skipped, TicketRunStatus.Aborted })
        {
            Assert.True(terminal.IsTerminal());
            Assert.All(Enum.GetValues<TicketRunStatus>(), to => Assert.False(terminal.CanTransitionTo(to)));
        }
    }

    [Fact]
    public void step_runs_pending_to_running_to_a_final_outcome()
    {
        Assert.True(StepStatus.Pending.CanTransitionTo(StepStatus.Running));
        foreach (StepStatus outcome in new[] { StepStatus.Succeeded, StepStatus.Failed, StepStatus.TimedOut, StepStatus.Cancelled, StepStatus.NeedsAttention })
        {
            Assert.True(StepStatus.Running.CanTransitionTo(outcome), $"Running -> {outcome}");
            Assert.False(outcome.CanTransitionTo(StepStatus.Running), $"{outcome} -> Running");
            Assert.False(outcome.IsActive());
        }
    }

    [Fact]
    public void step_pending_can_be_cancelled_but_not_succeed_directly()
    {
        Assert.True(StepStatus.Pending.CanTransitionTo(StepStatus.Cancelled));
        Assert.False(StepStatus.Pending.CanTransitionTo(StepStatus.Succeeded));
    }

    [Fact]
    public void only_pending_and_running_steps_are_active()
    {
        Assert.True(StepStatus.Pending.IsActive());
        Assert.True(StepStatus.Running.IsActive());
    }
}
