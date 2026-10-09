using WebDevLoop.Core.Domain;

namespace WebDevLoop.Core.Orchestration.Completion.ReadyAndMerge;

public enum MergeTrackingOutcome
{
    /// <summary>The stack is still open; polling continues.</summary>
    Open,

    /// <summary>Every PR is merged but trunk does not contain the top layer yet; polling continues until the timeout.</summary>
    AwaitingTrunk,

    /// <summary>Trunk contains the merged stack; the spec completed.</summary>
    Completed,

    /// <summary>The stack was closed unmerged, or trunk never received the merged top layer; the spec needs attention.</summary>
    NeedsAttention,

    /// <summary>The GitHub poll failed; the next pass retries.</summary>
    Faulted,

    /// <summary>The spec is not <c>AwaitingMerge</c> (or has no stack); nothing was polled.</summary>
    NotTracked,

    /// <summary>A save lost a compare-and-swap race; the other writer wins.</summary>
    ConcurrencyConflict,
}

public sealed record MergeTrackingResult(MergeTrackingOutcome Outcome, string? Reason = null)
{
    public static MergeTrackingResult NotTracked { get; } = new(MergeTrackingOutcome.NotTracked);

    public static MergeTrackingResult ConcurrencyConflict { get; } = new(MergeTrackingOutcome.ConcurrencyConflict);
}

/// <summary>Outcome of one periodic pass: completions resumed and merge polls, per spec run.</summary>
public sealed record MergeTrackingPass(
    IReadOnlyDictionary<RunId, CompletionResult> Completions,
    IReadOnlyDictionary<RunId, MergeTrackingResult> Merges);
