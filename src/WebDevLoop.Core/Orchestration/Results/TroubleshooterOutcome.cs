namespace WebDevLoop.Core.Orchestration.Results;

public enum TroubleshooterOutcome
{
    /// <summary>The agent claims it repaired the situation. Only a claim: WebDevLoop re-runs the failed check before it resumes the work.</summary>
    Resolved,

    /// <summary>The agent diagnosed the problem but only the user can fix or decide it.</summary>
    NeedsUser,

    /// <summary>The agent could not find out what is wrong or repair it.</summary>
    CannotResolve,
}
