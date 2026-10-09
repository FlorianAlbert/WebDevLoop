namespace WebDevLoop.Core.Orchestration.ReviewLoop;

/// <summary>
/// Identity of one two-axis review round, recorded with every axis result so a round can be resumed or read back.
/// </summary>
/// <param name="Attempt">Attempt of the reviewed work: the ticket's attempt, or the spec's review cycle for parent-spec reviews.</param>
/// <param name="Iteration">0-based number of review rounds completed before this one (<c>{review_iteration}</c>).</param>
public sealed record ReviewRound(int Attempt, int Iteration);
