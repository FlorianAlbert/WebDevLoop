using System.Text.RegularExpressions;
using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Queries;

namespace WebDevLoop.Web.Components.Queue;

public static partial class SpecRunDisplay
{
    /// <summary>"WaitingForDependency" becomes "Waiting for dependency".</summary>
    public static string Label(SpecRunStatus status)
    {
        string spaced = WordBoundary().Replace(status.ToString(), " ");
        return string.Concat(spaced[..1], spaced[1..].ToLowerInvariant());
    }

    public static string BadgeClass(SpecRunStatus status) => SpecRunLanes.For(status) switch
    {
        SpecRunLane.Active => "text-bg-primary",
        SpecRunLane.AwaitingMerge => "text-bg-info",
        SpecRunLane.NeedsAttention => "text-bg-danger",
        SpecRunLane.Waiting => "text-bg-secondary",
        _ => status == SpecRunStatus.Aborted ? "text-bg-dark" : "text-bg-success",
    };

    /// <summary>A short explanation of what a non-running spec is waiting for; <c>null</c> when there is nothing to add.</summary>
    public static string? Note(SpecRunView run, SpecDependencyMode effectiveMode) => run.Status switch
    {
        SpecRunStatus.Queued => "Waiting for a free active-spec slot",
        SpecRunStatus.WaitingForDependency => (run.DependencyModeUsed ?? effectiveMode) == SpecDependencyMode.WaitForMerge
            ? "Waiting for the dependency to be merged"
            : "Waiting until the dependency's stack is ready to build on",
        SpecRunStatus.ReadyForReview => "Ready for human review",
        SpecRunStatus.AwaitingMerge => "Stack is open; waiting for a human to merge it",
        SpecRunStatus.NeedsAttention => run.FailureReason ?? "Needs a human decision",
        SpecRunStatus.Aborted => run.FailureReason,
        _ => null,
    };

    [GeneratedRegex("(?<=[a-z])(?=[A-Z])")]
    private static partial Regex WordBoundary();
}
