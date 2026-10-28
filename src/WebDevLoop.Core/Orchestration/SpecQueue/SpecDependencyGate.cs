using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Ports;

namespace WebDevLoop.Core.Orchestration.SpecQueue;

/// <summary>Starts a parent only after all of its current native GitHub blockers are closed.</summary>
public sealed class SpecDependencyGate(IGitHubIssues issues)
{
    private readonly ParentBlockerReader _blockers = new(issues.GetIssueAsync);

    public async Task<SpecStartDecision> EvaluateAsync(SpecRun run, CancellationToken cancellationToken)
    {
        return await _blockers.AreAllSatisfiedAsync(run.ParentIssue, cancellationToken)
            ? SpecStartDecision.FromTrunk
            : SpecStartDecision.Wait;
    }
}
