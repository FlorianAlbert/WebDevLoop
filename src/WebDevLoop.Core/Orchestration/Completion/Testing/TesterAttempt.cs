using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Orchestration.Results;

namespace WebDevLoop.Core.Orchestration.Completion.Testing;

internal enum TesterAttemptOutcome
{
    /// <summary>The tester step succeeded with a report.</summary>
    Reported,

    /// <summary>The tester step failed or timed out (including an application that never became ready); worth a retry.</summary>
    Failed,

    Cancelled,

    /// <summary>No port of the configured range is free; nothing was started.</summary>
    NoFreePort,

    /// <summary>The tester prompt cannot be rendered; nothing was started.</summary>
    PromptInvalid,

    /// <summary>Another runner claimed the step or lease first, or a save lost a race.</summary>
    ConcurrencyConflict,
}

/// <param name="StepRunId">The tester step; null when nothing was started.</param>
/// <param name="Report">The accepted report; set exactly when <paramref name="Outcome"/> is <see cref="TesterAttemptOutcome.Reported"/>.</param>
internal sealed record TesterAttempt(TesterAttemptOutcome Outcome, StepRunId? StepRunId = null, TestReport? Report = null, string? Failure = null);
