namespace WebDevLoop.Core.Orchestration.TicketExecution;

/// <summary>
/// Starts claimed implementation work in the background so dispatch never waits for an implementer to finish.
/// Implementations run <see cref="TicketImplementationRunner.RunAsync"/> in a fresh unit-of-work scope and must return
/// immediately. Launching the same assignment twice is safe: the runner starts at most one implementer session per ticket.
/// </summary>
public interface IImplementationLauncher
{
    void Launch(ImplementationAssignment assignment);
}
