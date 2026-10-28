using WebDevLoop.Core.Ports;

namespace WebDevLoop.Core.Domain;

/// <summary>Explicit, per-issue permission for WebDevLoop, independent of triage and dependencies.</summary>
public static class WebDevLoopOptIn
{
    public const string Label = "webdevloop";

    /// <summary>Whether this issue carries the opt-in label, regardless of its state.</summary>
    public static bool HasOptIn(this IssueSnapshot issue) =>
        issue.Labels.Contains(Label, StringComparer.OrdinalIgnoreCase);

    /// <summary>Whether this issue is open and independently opted in for work selection.</summary>
    public static bool IsOpenAndOptedIn(this IssueSnapshot issue) =>
        issue.State == IssueState.Open && issue.HasOptIn();
}
