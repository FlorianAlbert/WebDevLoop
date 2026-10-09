using WebDevLoop.Core.Domain;

namespace WebDevLoop.Core.Orchestration.ReviewLoop;

/// <summary>One two-axis review round of a ticket branch or of a spec's integration branch.</summary>
/// <param name="TicketRunId">The reviewed ticket; required for <see cref="ReviewScope.Ticket"/>, null for <see cref="ReviewScope.ParentSpec"/>.</param>
/// <param name="Axes">Axes still to review in this round (normally both; fewer when resuming a partly reviewed round).</param>
/// <param name="CreatesCheckout">
/// The runner checks out the target branch at the reviewed commit into the target's working directory after claiming the
/// reviewer steps and removes it before finishing them (the parent-spec review's run-scoped checkout); false when the
/// working directory already exists (a ticket worktree).
/// </param>
public sealed record ReviewRequest(
    RunId SpecRunId,
    TicketRunId? TicketRunId,
    ReviewScope Scope,
    ReviewTarget Target,
    ReviewRound Round,
    IReadOnlyCollection<FindingAxis> Axes,
    bool CreatesCheckout = false)
{
    public static IReadOnlyList<FindingAxis> BothAxes { get; } = [FindingAxis.CodingStandards, FindingAxis.Specification];
}
