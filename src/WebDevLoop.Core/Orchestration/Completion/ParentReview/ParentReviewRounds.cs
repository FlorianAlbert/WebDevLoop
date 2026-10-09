using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Orchestration.Results;
using WebDevLoop.Core.Orchestration.ReviewLoop;

namespace WebDevLoop.Core.Orchestration.Completion.ParentReview;

/// <summary>The axis report of a succeeded parent-review step and the step that produced it (the findings' source).</summary>
internal sealed record ParentReviewAxisResult(StepRunId StepRunId, ReviewReport Report);

/// <summary>Identifies parent-spec review rounds and reads their persisted axis results back from the spec's steps.</summary>
internal static class ParentReviewRounds
{
    /// <summary>One round per review cycle: the cycle is the attempt, and the cycles completed before it the iteration.</summary>
    public static ReviewRound Current(SpecRun spec) => new(spec.ReviewCycle, Math.Max(0, spec.ReviewCycle - 1));

    /// <returns>The latest result per axis that reviewed exactly <paramref name="reviewedHead"/> in <paramref name="round"/>.</returns>
    public static IReadOnlyDictionary<FindingAxis, ParentReviewAxisResult> Read(IEnumerable<StepRun> steps, ReviewRound round, CommitSha reviewedHead)
    {
        var results = new Dictionary<FindingAxis, ParentReviewAxisResult>();
        foreach (StepRun step in steps
            .Where(step => step is { Kind: StepKind.ParentReview, Status: StepStatus.Succeeded, TicketRunId: null })
            .OrderBy(step => step.Attempt))
        {
            if (ReviewStepRecord.TryParse(step.StructuredResultJson) is { } record && record.Matches(round, reviewedHead))
            {
                results[record.Axis] = new ParentReviewAxisResult(step.Id, record.ToReport());
            }
        }

        return results;
    }
}
