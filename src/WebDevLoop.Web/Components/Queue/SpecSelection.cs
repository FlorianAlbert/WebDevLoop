using WebDevLoop.Core.Management;
using WebDevLoop.Core.Orchestration.SpecQueue;
using WebDevLoop.Core.Queries;

namespace WebDevLoop.Web.Components.Queue;

/// <summary>Pure selection updates that preserve the chosen queue order and never mutate their input.</summary>
public static class SpecSelection
{
    public static IReadOnlyList<SpecCandidateView> Toggle(IReadOnlyList<SpecCandidateView> selection, SpecCandidateView candidate) =>
        selection.Any(item => item.IssueNumber == candidate.IssueNumber)
            ? Remove(selection, candidate.IssueNumber)
            : [.. selection, candidate];

    public static IReadOnlyList<SpecCandidateView> Move(IReadOnlyList<SpecCandidateView> selection, int issueNumber, int direction)
    {
        var reordered = selection.ToArray();
        int index = Array.FindIndex(reordered, item => item.IssueNumber == issueNumber);
        int target = index + direction;
        if (index >= 0 && target >= 0 && target < reordered.Length)
        {
            (reordered[index], reordered[target]) = (reordered[target], reordered[index]);
        }

        return reordered;
    }

    public static IReadOnlyList<SpecCandidateView> Remove(IReadOnlyList<SpecCandidateView> selection, int issueNumber) =>
        selection.Where(item => item.IssueNumber != issueNumber).ToArray();

    public static IReadOnlyList<SpecCandidateView> RemoveHandled(
        IReadOnlyList<SpecCandidateView> selection, IReadOnlyList<SpecSelectionResult> results)
    {
        var handled = results.Where(result => result.Outcome.IsHandled()).Select(result => result.SpecIssueNumber).ToHashSet();
        return selection.Where(item => !handled.Contains(item.IssueNumber)).ToArray();
    }

    public static IReadOnlyList<SpecCandidateView> AppendCandidates(
        IReadOnlyList<SpecCandidateView> existing, IReadOnlyList<SpecCandidateView> additions) =>
        existing.Concat(additions).DistinctBy(item => item.IssueNumber).ToArray();
}
