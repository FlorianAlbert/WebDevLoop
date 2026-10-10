using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Orchestration.Results;

namespace WebDevLoop.Core.Orchestration.ReviewLoop;

public enum ReviewRoundOutcome
{
    /// <summary>Every requested axis reported; see <see cref="ReviewRoundResult.Reports"/>.</summary>
    Completed,

    /// <summary>The prompt could not be rendered, the target is unusable, or an axis failed every allowed attempt.</summary>
    Failed,

    /// <summary>A reviewer turn was cancelled (e.g. the run was aborted).</summary>
    Cancelled,

    /// <summary>Another runner already claimed this round (or a save lost a compare-and-swap race).</summary>
    ConcurrencyConflict,
}

/// <param name="Reports">One report per requested axis when <see cref="Outcome"/> is <see cref="ReviewRoundOutcome.Completed"/>.</param>
public sealed record ReviewRoundResult(ReviewRoundOutcome Outcome, IReadOnlyList<ReviewReport> Reports, string? Reason = null, AttentionReason? Attention = null)
{
    public static ReviewRoundResult ConcurrencyConflict { get; } = new(ReviewRoundOutcome.ConcurrencyConflict, []);

    public bool IsClean => Outcome == ReviewRoundOutcome.Completed && Reports.All(report => report.Verdict == ReviewVerdict.Clean);

    public IReadOnlyList<Finding> Findings => Reports.SelectMany(report => report.Findings).ToArray();

    public static ReviewRoundResult Failed(AttentionReason reason) => new(ReviewRoundOutcome.Failed, [], reason.Details, reason);
}
