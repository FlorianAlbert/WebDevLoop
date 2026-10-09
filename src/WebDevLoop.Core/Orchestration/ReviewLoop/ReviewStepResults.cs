using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Orchestration.Results;

namespace WebDevLoop.Core.Orchestration.ReviewLoop;

/// <summary>Reads the persisted axis reports of a review round back from its succeeded review steps.</summary>
public static class ReviewStepResults
{
    /// <param name="steps">Steps of the reviewed ticket, or of the spec for parent-spec reviews.</param>
    /// <param name="kind"><see cref="StepKind.Review"/> or <see cref="StepKind.ParentReview"/>.</param>
    /// <returns>The latest report per axis that reviewed exactly <paramref name="reviewedHead"/> in <paramref name="round"/>.</returns>
    public static IReadOnlyDictionary<FindingAxis, ReviewReport> Read(
        IEnumerable<StepRun> steps,
        StepKind kind,
        ReviewRound round,
        CommitSha reviewedHead)
    {
        ArgumentNullException.ThrowIfNull(steps);
        ArgumentNullException.ThrowIfNull(round);
        var reports = new Dictionary<FindingAxis, ReviewReport>();
        foreach (StepRun step in steps.Where(step => step.Kind == kind && step.Status == StepStatus.Succeeded).OrderBy(step => step.Attempt))
        {
            if (ReviewStepRecord.TryParse(step.StructuredResultJson) is { } record && record.Matches(round, reviewedHead))
            {
                reports[record.Axis] = record.ToReport();
            }
        }

        return reports;
    }

    /// <summary>The current round of a ticket in review: its attempt, the rounds completed so far, and the implemented head.</summary>
    public static IReadOnlyDictionary<FindingAxis, ReviewReport> ReadCurrentRound(TicketRun ticket, IEnumerable<StepRun> steps)
    {
        ArgumentNullException.ThrowIfNull(ticket);
        return ticket.LastImplementedSha is { } head
            ? Read(steps, StepKind.Review, CurrentRound(ticket), head)
            : new Dictionary<FindingAxis, ReviewReport>();
    }

    public static ReviewRound CurrentRound(TicketRun ticket)
    {
        ArgumentNullException.ThrowIfNull(ticket);
        return new ReviewRound(ticket.Attempt, ticket.ReviewIteration);
    }
}
