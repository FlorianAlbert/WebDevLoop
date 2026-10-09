using WebDevLoop.Core.Orchestration.TicketExecution;

namespace WebDevLoop.Core.Tests.Orchestration.TicketExecution;

/// <summary>
/// Records launches and, once <see cref="Run"/> is set, starts each one without awaiting it (like a background worker),
/// so dispatch continues while launched implementers are still waiting for their agent.
/// </summary>
internal sealed class TestImplementationLauncher : IImplementationLauncher
{
    private readonly List<Task> _running = [];

    public List<ImplementationAssignment> Launched { get; } = [];

    public Func<ImplementationAssignment, Task>? Run { get; set; }

    public void Launch(ImplementationAssignment assignment)
    {
        Launched.Add(assignment);
        if (Run is not null)
        {
            _running.Add(Run(assignment));
        }
    }

    public Task WhenAllFinishedAsync() => Task.WhenAll(_running);
}
