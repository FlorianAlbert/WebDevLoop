namespace WebDevLoop.Core.Orchestration.Recovery.AgentSteps;

/// <param name="StallGracePeriod">
/// How long a ticket or spec may sit in a working status without an active step and without any activity in this process
/// before recovery relaunches its work (it may legitimately be between two steps). Work left over by a previous process is
/// relaunched immediately. A step still running this long past its timeout has lost its runner and is finished as well.
/// </param>
public sealed record AgentStepRecoveryOptions(TimeSpan StallGracePeriod)
{
    public static AgentStepRecoveryOptions Default { get; } = new(TimeSpan.FromMinutes(5));
}
