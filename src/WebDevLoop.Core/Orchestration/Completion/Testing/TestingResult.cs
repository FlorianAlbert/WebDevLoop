using WebDevLoop.Core.Orchestration.Findings;

namespace WebDevLoop.Core.Orchestration.Completion.Testing;

public enum TestingOutcome
{
    /// <summary>The tester passed the application; <see cref="SpecTestingPassed"/> hands the spec to completion.</summary>
    Passed,

    /// <summary>Test issues became finding tickets and the spec moved back to <c>Running</c> to work them.</summary>
    FindingTicketsCreated,

    /// <summary>
    /// The tester still found issues in the last allowed cycle (<c>TesterCycleLimit</c>). Finding tickets were created, but
    /// the spec needs attention instead of looping again.
    /// </summary>
    CycleLimitReached,

    /// <summary>Every issue repeats one whose ticket is already done, so working tickets cannot fix it; the spec needs attention.</summary>
    NoNewWork,

    /// <summary>The tester could not test the application (environment or run instructions); the spec needs attention.</summary>
    Blocked,

    /// <summary>The tester run could not start or failed every attempt; the spec needs attention.</summary>
    Failed,

    /// <summary>The tester turn was cancelled (e.g. the run was aborted); the spec is left to whoever cancelled it.</summary>
    Cancelled,

    /// <summary>Another runner is already testing this spec; nothing was started.</summary>
    AlreadyRunning,

    /// <summary>The spec is not <c>Testing</c>; nothing was started.</summary>
    NotTesting,

    /// <summary>A save lost a compare-and-swap race (duplicate runner or concurrent abort); the other writer wins.</summary>
    ConcurrencyConflict,
}

/// <param name="Tickets">The finding tickets of this cycle (new or reused); empty unless test issues were issued.</param>
public sealed record TestingResult(TestingOutcome Outcome, IReadOnlyList<FindingTicket> Tickets, string? Reason = null)
{
    public static TestingResult AlreadyRunning { get; } = new(TestingOutcome.AlreadyRunning, []);

    public static TestingResult NotTesting { get; } = new(TestingOutcome.NotTesting, []);

    public static TestingResult ConcurrencyConflict { get; } = new(TestingOutcome.ConcurrencyConflict, []);
}
