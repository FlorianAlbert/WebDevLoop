using WebDevLoop.Core.Agents;
using WebDevLoop.Core.Domain;

namespace WebDevLoop.Core.Ports;

public interface IIdGenerator
{
    RunId NewRunId();

    TicketRunId NewTicketRunId();

    StepRunId NewStepRunId();

    AgentSessionId NewAgentSessionId(StepRunId stepRunId);
}
