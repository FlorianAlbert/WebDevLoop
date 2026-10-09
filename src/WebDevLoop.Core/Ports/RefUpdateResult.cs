using WebDevLoop.Core.Domain;

namespace WebDevLoop.Core.Ports;

/// <param name="ActualTip">The ref's tip after the call (null if the ref does not exist).</param>
public sealed record RefUpdateResult(RefUpdateOutcome Outcome, CommitSha? ActualTip)
{
    public bool Succeeded => Outcome is RefUpdateOutcome.Updated or RefUpdateOutcome.AlreadyAtTarget;
}
