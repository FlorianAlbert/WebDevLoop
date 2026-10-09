using WebDevLoop.Core.Ports;

namespace WebDevLoop.Core.Orchestration.Recovery.AgentSteps;

/// <summary>
/// When this app process started (register as a singleton created at startup). A step started earlier was started by a
/// previous process, so no live session of this process owns it, even if it is still persisted as running.
/// </summary>
public sealed record ProcessBoot(DateTimeOffset StartedAt)
{
    public static ProcessBoot Now(IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        return new ProcessBoot(clock.UtcNow);
    }

    public bool IsBeforeBoot(DateTimeOffset at) => at < StartedAt;
}
