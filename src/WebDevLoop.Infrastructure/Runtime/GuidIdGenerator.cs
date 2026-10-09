using WebDevLoop.Core.Agents;
using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Ports;

namespace WebDevLoop.Infrastructure.Runtime;

/// <summary>
/// Short random ids prefixed by their kind. They become branch segments (<c>webdevloop/&lt;run&gt;/ticket/&lt;ticket&gt;</c>),
/// so they stay lowercase hex; 48 random bits keep collisions negligible for one app's history.
/// </summary>
public sealed class GuidIdGenerator : IIdGenerator
{
    private const int RandomHexLength = 12;
    private const string SessionPrefix = "webdevloop-";

    public RunId NewRunId() => new(NewId('r'));

    public TicketRunId NewTicketRunId() => new(NewId('t'));

    public StepRunId NewStepRunId() => new(NewId('s'));

    /// <summary>Derived from the step, so a recovered step resumes the same Copilot session.</summary>
    public AgentSessionId NewAgentSessionId(StepRunId stepRunId) => new(SessionPrefix + stepRunId.Value);

    private static string NewId(char prefix) => prefix + Guid.NewGuid().ToString("N")[..RandomHexLength];
}
