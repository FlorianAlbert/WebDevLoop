using WebDevLoop.Core.Agents;
using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Ports;

namespace WebDevLoop.Core.Tests.Ports.Fakes;

public sealed class SequentialIdGenerator : IIdGenerator
{
    private int _next;

    public RunId NewRunId() => new($"run{Next()}");

    public TicketRunId NewTicketRunId() => new($"ticket{Next()}");

    public StepRunId NewStepRunId() => new($"step{Next()}");

    public AgentSessionId NewAgentSessionId(StepRunId stepRunId) => new($"session-{stepRunId}");

    private int Next() => Interlocked.Increment(ref _next);
}
