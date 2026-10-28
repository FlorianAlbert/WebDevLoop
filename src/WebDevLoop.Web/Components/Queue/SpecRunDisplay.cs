using System.Text.RegularExpressions;
using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Queries;
using WebDevLoop.Web.Components.Shared;

namespace WebDevLoop.Web.Components.Queue;

public static partial class SpecRunDisplay
{
    /// <summary>"WaitingForDependency" becomes "Waiting for dependency".</summary>
    public static string Label(SpecRunStatus status)
    {
        string spaced = WordBoundary().Replace(status.ToString(), " ");
        return string.Concat(spaced[..1], spaced[1..].ToLowerInvariant());
    }

    public static StatusVariant Variant(SpecRunStatus status) => SpecRunLanes.For(status) switch
    {
        SpecRunLane.Active => StatusVariant.Info,
        SpecRunLane.AwaitingMerge => StatusVariant.Info,
        SpecRunLane.NeedsAttention => StatusVariant.Warning,
        SpecRunLane.Waiting => StatusVariant.Neutral,
        _ => status == SpecRunStatus.Aborted ? StatusVariant.Danger : StatusVariant.Success,
    };

    /// <summary>A short explanation of what a non-running spec is waiting for; <c>null</c> when there is nothing to add.</summary>
    public static string? Note(SpecRunView run, IReadOnlyList<ParentBlockerView>? blockers = null) => run.WorkDecisionReason ?? (run.Status switch
    {
        SpecRunStatus.Queued => "Waiting for a free active-spec slot",
        SpecRunStatus.WaitingForDependency => WithBlockers("Waiting for all native GitHub blockers to close", blockers),
        SpecRunStatus.ReadyForReview => "Ready for human review",
        SpecRunStatus.AwaitingMerge => "Stack is open; waiting for a human to merge it",
        SpecRunStatus.NeedsAttention => AttentionDisplay.Headline(run.Attention, run.FailureReason) ?? "Needs a human decision",
        SpecRunStatus.Aborted => run.FailureReason,
        _ => null,
    });

    private static string WithBlockers(string note, IReadOnlyList<ParentBlockerView>? blockers)
    {
        string[] openBlockers = (blockers ?? []).Where(blocker => !blocker.IsSatisfied).Select(Describe).ToArray();
        return openBlockers.Length == 0 ? note : $"{note}: {string.Join(", ", openBlockers)}";
    }

    private static string Describe(ParentBlockerView blocker) =>
        $"{blocker.Issue} ({(blocker.Status is { } status ? Label(status) : "not tracked")})";

    [GeneratedRegex("(?<=[a-z])(?=[A-Z])")]
    private static partial Regex WordBoundary();
}
