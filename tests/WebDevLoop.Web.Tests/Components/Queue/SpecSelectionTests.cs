using WebDevLoop.Core.Management;
using WebDevLoop.Core.Orchestration.SpecQueue;
using WebDevLoop.Core.Queries;
using WebDevLoop.Web.Components.Queue;

namespace WebDevLoop.Web.Tests.Components.Queue;

public sealed class SpecSelectionTests
{
    [Fact]
    public void pruning_removes_only_new_or_existing_runs_and_retains_failures_for_retry()
    {
        SpecCandidateView[] original = [new(100, "First"), new(200, "Second"), new(300, "Third")];
        SpecSelectionResult[] results =
        [
            new(100, new(EnqueueOutcome.Queued)),
            new(200, new(EnqueueOutcome.Ineligible, Reason: "Opt in first.")),
            new(300, new(EnqueueOutcome.AlreadyQueued)),
        ];

        IReadOnlyList<SpecCandidateView> remaining = SpecSelection.RemoveHandled(original, results);

        Assert.Equal([200], remaining.Select(item => item.IssueNumber));
        Assert.Equal(3, original.Length);
    }

    [Fact]
    public void appending_pages_deduplicates_by_issue_number_without_changing_existing_order()
    {
        SpecCandidateView[] original = [new(100, "First"), new(200, "Second")];

        IReadOnlyList<SpecCandidateView> combined = SpecSelection.AppendCandidates(
            original, [new(200, "Updated title"), new(300, "Third"), new(300, "Repeated")]);

        Assert.Equal([100, 200, 300], combined.Select(item => item.IssueNumber));
        Assert.Equal("Second", combined[1].Title);
        Assert.Equal(2, original.Length);
    }

    [Fact]
    public void toggling_adds_to_the_end_and_removes_by_issue_number_without_mutating_the_original()
    {
        SpecCandidateView[] original = [new(100, "First")];

        IReadOnlyList<SpecCandidateView> added = SpecSelection.Toggle(original, new(200, "Second"));
        IReadOnlyList<SpecCandidateView> removed = SpecSelection.Toggle(added, new(100, "Updated title"));

        Assert.Equal([100, 200], added.Select(item => item.IssueNumber));
        Assert.Equal([200], removed.Select(item => item.IssueNumber));
        Assert.Equal([100], original.Select(item => item.IssueNumber));
    }

    [Fact]
    public void moving_swaps_neighbors_and_stops_at_selection_boundaries()
    {
        SpecCandidateView[] original = [new(100, "First"), new(200, "Second"), new(300, "Third")];

        IReadOnlyList<SpecCandidateView> moved = SpecSelection.Move(original, 300, -1);
        IReadOnlyList<SpecCandidateView> restored = SpecSelection.Move(moved, 300, 1);

        Assert.Equal([100, 300, 200], moved.Select(item => item.IssueNumber));
        Assert.Equal([100, 200, 300], restored.Select(item => item.IssueNumber));
        Assert.Equal(original, SpecSelection.Move(original, 100, -1));
        Assert.Equal(original, SpecSelection.Move(original, 300, 1));
        Assert.Equal(original, SpecSelection.Move(original, 999, -1));
    }

    [Fact]
    public void removing_keeps_the_remaining_order()
    {
        SpecCandidateView[] original = [new(100, "First"), new(200, "Second"), new(300, "Third")];

        IReadOnlyList<SpecCandidateView> remaining = SpecSelection.Remove(original, 200);

        Assert.Equal([100, 300], remaining.Select(item => item.IssueNumber));
        Assert.Equal(3, original.Length);
    }
}
