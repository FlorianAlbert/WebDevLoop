using WebDevLoop.Core.Agents;
using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Orchestration.Results;
using WebDevLoop.Core.Ports;

namespace WebDevLoop.Core.Tests.Ports.Fakes;

/// <summary>
/// Plays scripted agent turns per role. Scripts build the report the way the real runner parses a report-tool payload,
/// so domain validation failures surface as <see cref="AgentRunOutcome.InvalidReport"/>.
/// </summary>
public sealed class ScriptedAgentRunner : IAgentRunner
{
    private readonly Dictionary<AgentRole, Queue<Func<AgentRunRequest, AgentReport>>> _scripts = [];

    public List<AgentRunRequest> Started { get; } = [];

    public List<AgentRunRequest> Resumed { get; } = [];

    public List<AgentSessionId> Aborted { get; } = [];

    public ScriptedAgentRunner Script(AgentRole role, Func<AgentRunRequest, AgentReport> turn)
    {
        if (!_scripts.TryGetValue(role, out Queue<Func<AgentRunRequest, AgentReport>>? turns))
        {
            _scripts[role] = turns = new Queue<Func<AgentRunRequest, AgentReport>>();
        }

        turns.Enqueue(turn);
        return this;
    }

    public Task<AgentRunResult> StartAsync(AgentRunRequest request, CancellationToken cancellationToken)
    {
        Started.Add(request);
        return Task.FromResult(Play(request));
    }

    public Task<AgentRunResult> ResumeAsync(AgentRunRequest request, CancellationToken cancellationToken)
    {
        Resumed.Add(request);
        return Task.FromResult(Play(request));
    }

    public Task AbortAsync(AgentSessionId sessionId, CancellationToken cancellationToken)
    {
        Aborted.Add(sessionId);
        return Task.CompletedTask;
    }

    private AgentRunResult Play(AgentRunRequest request)
    {
        if (!_scripts.TryGetValue(request.Role, out Queue<Func<AgentRunRequest, AgentReport>>? turns) || turns.Count == 0)
        {
            return AgentRunResult.NotReported(AgentRunOutcome.MissingReport, $"No scripted turn for {request.Role}.");
        }

        try
        {
            return AgentRunResult.Reported(turns.Dequeue()(request));
        }
        catch (InvalidAgentReportException exception)
        {
            return AgentRunResult.NotReported(AgentRunOutcome.InvalidReport, exception.Message);
        }
    }
}
