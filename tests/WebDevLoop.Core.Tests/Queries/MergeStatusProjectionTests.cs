using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Queries;

namespace WebDevLoop.Core.Tests.Queries;

public sealed class MergeStatusProjectionTests
{
    private static readonly DateTimeOffset At = new(2026, 10, 9, 12, 0, 0, TimeSpan.Zero);

    private static readonly StackLayerView[] Stack =
    [
        new(1, "t-1", "stack/run-1/t-1", "main", "aaa", 11, 5, false, "aaa"),
        new(2, "t-2", "stack/run-1/t-2", "stack/run-1/t-1", "bbb", 12, 5, false, "bbb"),
    ];

    private static SpecRun Spec(params SpecRunStatus[] path)
    {
        SpecRun spec = SpecRun.Queue(new RunId("run-1"), 1, new IssueRef("o", "r", 5), "Spec", "body", 1, At);
        foreach (SpecRunStatus next in path)
        {
            spec.TransitionTo(next, At);
        }

        return spec;
    }

    private static readonly SpecRunStatus[] ToAwaitingMerge =
    [
        SpecRunStatus.Preparing, SpecRunStatus.Running, SpecRunStatus.ParentReviewing, SpecRunStatus.Testing,
        SpecRunStatus.ReadyForReview, SpecRunStatus.AwaitingMerge,
    ];

    [Fact]
    public void a_stack_waiting_for_the_human_merge_is_awaiting_and_lists_its_pull_requests()
    {
        MergeStatusView status = MergeStatusProjection.From(Spec(ToAwaitingMerge).ToView(), Stack);

        Assert.Equal(("run-1", MergeState.Awaiting, SpecRunStatus.AwaitingMerge), (status.SpecRunId, status.State, status.SpecStatus));
        Assert.Equal([11, 12], status.PullRequests);
        Assert.Equal(("bbb", (DateTimeOffset?)At, (DateTimeOffset?)null, (string?)null), (status.TopCommitSha, status.ReadyAt, status.CompletedAt, status.Detail));
    }

    [Fact]
    public void a_completed_run_with_a_stack_is_merged()
    {
        MergeStatusView status = MergeStatusProjection.From(Spec([.. ToAwaitingMerge, SpecRunStatus.Completed]).ToView(), Stack);

        Assert.Equal((MergeState.Merged, At), (status.State, status.CompletedAt));
    }

    [Fact]
    public void a_stack_closed_unmerged_is_closed_with_the_reason()
    {
        SpecRun spec = Spec(ToAwaitingMerge);
        spec.MarkNeedsAttention("The PR stack #11, #12 was closed without being merged into 'main'.", At);

        MergeStatusView status = MergeStatusProjection.From(spec.ToView(), Stack);

        Assert.Equal((MergeState.Closed, SpecRunStatus.NeedsAttention), (status.State, status.SpecStatus));
        Assert.Contains("closed without being merged", status.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void a_run_that_needs_attention_before_its_stack_was_ready_is_not_ready()
    {
        SpecRun spec = Spec(SpecRunStatus.Preparing, SpecRunStatus.Running, SpecRunStatus.ParentReviewing, SpecRunStatus.Testing);
        spec.MarkNeedsAttention("tester failed", At);

        Assert.Equal(MergeState.NotReady, MergeStatusProjection.From(spec.ToView(), Stack).State);
    }

    [Theory]
    [InlineData(SpecRunStatus.ReadyForReview, MergeState.Awaiting)]
    [InlineData(SpecRunStatus.Running, MergeState.NotReady)]
    [InlineData(SpecRunStatus.Aborted, MergeState.Aborted)]
    public void other_statuses_map_to_their_merge_state(SpecRunStatus last, MergeState expected)
    {
        SpecRunStatus[] path = last switch
        {
            SpecRunStatus.ReadyForReview => ToAwaitingMerge[..^1],
            SpecRunStatus.Running => [SpecRunStatus.Preparing, SpecRunStatus.Running],
            _ => [last],
        };

        Assert.Equal(expected, MergeStatusProjection.From(Spec(path).ToView(), Stack).State);
    }

    [Fact]
    public void a_run_completed_without_pull_requests_says_so()
    {
        SpecRun spec = Spec(SpecRunStatus.Preparing, SpecRunStatus.Running, SpecRunStatus.ParentReviewing, SpecRunStatus.Testing, SpecRunStatus.Completed);

        MergeStatusView status = MergeStatusProjection.From(spec.ToView(), []);

        Assert.Equal(MergeState.CompletedWithoutPullRequests, status.State);
        Assert.Empty(status.PullRequests);
        Assert.Null(status.TopCommitSha);
    }

    [Fact]
    public void views_expose_the_phase_that_needs_attention()
    {
        SpecRun spec = Spec(SpecRunStatus.Preparing);
        spec.MarkNeedsAttention("clone failed", At);
        TicketRun ticket = TicketRun.Create(new TicketRunId("t-1"), spec.Id, new IssueRef("o", "r", 6), "Ticket", "body", At);
        ticket.TransitionTo(TicketRunStatus.Ready, At);
        ticket.MarkNeedsAttention("stuck", At);

        Assert.Equal(SpecRunStatus.Preparing, spec.ToView().NeedsAttentionFrom);
        Assert.Equal(TicketRunStatus.Ready, ticket.ToView([]).NeedsAttentionFrom);
    }
}
