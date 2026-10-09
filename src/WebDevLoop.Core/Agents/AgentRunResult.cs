using WebDevLoop.Core.Orchestration.Results;

namespace WebDevLoop.Core.Agents;

public sealed record AgentRunResult
{
    private AgentRunResult(AgentRunOutcome outcome, AgentReport? report, string? failureReason)
    {
        Outcome = outcome;
        Report = report;
        FailureReason = failureReason;
    }

    public AgentRunOutcome Outcome { get; }

    /// <summary>Present exactly when <see cref="Outcome"/> is <see cref="AgentRunOutcome.Reported"/>.</summary>
    public AgentReport? Report { get; }

    public string? FailureReason { get; }

    public static AgentRunResult Reported(AgentReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        return new(AgentRunOutcome.Reported, report, null);
    }

    public static AgentRunResult NotReported(AgentRunOutcome outcome, string failureReason)
    {
        if (outcome == AgentRunOutcome.Reported)
        {
            throw new ArgumentException("Use Reported(report) for a reported outcome.", nameof(outcome));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(failureReason);
        return new(outcome, null, failureReason);
    }
}
