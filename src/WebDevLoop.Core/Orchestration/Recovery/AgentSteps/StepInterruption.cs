using WebDevLoop.Core.Domain;

namespace WebDevLoop.Core.Orchestration.Recovery.AgentSteps;

/// <summary>Failure reasons of steps that recovery finished because no live session owned them any more.</summary>
public static class StepInterruption
{
    public const string ReasonPrefix = "Recovery: ";

    public const string Restarted = ReasonPrefix + "WebDevLoop restarted while this step was running; no live session owns it any more.";

    public static string Overdue(DateTimeOffset timeoutAt) =>
        $"{ReasonPrefix}the step was still running well past its timeout ({timeoutAt:O}); its runner is gone, so its session was aborted.";

    public static string LeaseExpired(int port, int killedProcesses) =>
        $"{ReasonPrefix}the tester lease on port {port} expired; {killedProcesses} leftover process(es) were killed.";

    public static string LeaseReleased(int port, int killedProcesses) =>
        $"{ReasonPrefix}WebDevLoop restarted while the tester ran on port {port}; {killedProcesses} leftover process(es) were killed.";

    public static bool IsInterruption(StepRun step)
    {
        ArgumentNullException.ThrowIfNull(step);
        return step.FailureReason?.StartsWith(ReasonPrefix, StringComparison.Ordinal) == true;
    }
}
