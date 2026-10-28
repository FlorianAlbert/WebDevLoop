using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Orchestration.SpecQueue;

namespace WebDevLoop.Core.Management;

/// <summary>A spec's enqueue result and actionable feedback, independent of HTTP presentation.</summary>
public sealed record SpecSelectionResult(int SpecIssueNumber, EnqueueResult Result)
{
    public EnqueueOutcome Outcome => Result.Outcome;

    public RunId? SpecRunId => Result.SpecRunId;

    public string Message => Outcome switch
    {
        EnqueueOutcome.Queued => $"Queued #{SpecIssueNumber}.",
        EnqueueOutcome.AlreadyQueued => $"#{SpecIssueNumber} is already queued.",
        EnqueueOutcome.RepositoryNotFound => "This repository no longer exists. Select a connected repository.",
        EnqueueOutcome.Ineligible or EnqueueOutcome.GitHubUnavailable or EnqueueOutcome.Unavailable or EnqueueOutcome.Failed => Result.Reason!,
        EnqueueOutcome.ConcurrencyConflict => "The queue changed concurrently. Refresh the queue and retry.",
        _ => throw new InvalidOperationException($"Unexpected enqueue outcome: {Outcome}."),
    };
}
