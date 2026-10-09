using WebDevLoop.Core.Agents;
using WebDevLoop.Core.Ports;

namespace WebDevLoop.Core.Tests.Orchestration.TicketExecution;

/// <summary>
/// Implementer sessions that stay in flight until the test replies, so several implementers can run at once and the
/// test decides when (and with what) each one finishes.
/// </summary>
internal sealed class ImplementerAgentStub : IAgentRunner
{
    private readonly Dictionary<AgentSessionId, (AgentRunRequest Request, TaskCompletionSource<AgentRunResult> Reply)> _inFlight = [];

    public List<AgentRunRequest> Started { get; } = [];

    public List<AgentRunRequest> Resumed { get; } = [];

    /// <summary>Sessions whose persisted state is gone: resuming them reports <see cref="AgentRunOutcome.SessionNotFound"/>.</summary>
    public HashSet<AgentSessionId> MissingSessions { get; } = [];

    /// <summary>When set, every started turn finishes immediately with this result.</summary>
    public Func<AgentRunRequest, AgentRunResult>? AutoReply { get; set; }

    public IEnumerable<AgentRunRequest> InFlight => _inFlight.Values.Select(entry => entry.Request);

    public Task<AgentRunResult> StartAsync(AgentRunRequest request, CancellationToken cancellationToken)
    {
        Started.Add(request);
        return Run(request);
    }

    /// <summary>A resumed turn behaves like a started one unless its session is missing.</summary>
    public Task<AgentRunResult> ResumeAsync(AgentRunRequest request, CancellationToken cancellationToken)
    {
        Resumed.Add(request);
        return MissingSessions.Contains(request.SessionId)
            ? Task.FromResult(AgentRunResult.NotReported(AgentRunOutcome.SessionNotFound, $"Session {request.SessionId} does not exist."))
            : Run(request);
    }

    public Task AbortAsync(AgentSessionId sessionId, CancellationToken cancellationToken) => Task.CompletedTask;

    public AgentRunRequest InFlightIn(string worktreePath) =>
        InFlight.Single(request => request.Policy.Paths.WorkingDirectory == worktreePath);

    public void Reply(AgentRunRequest request, AgentRunResult result)
    {
        TaskCompletionSource<AgentRunResult> reply = _inFlight[request.SessionId].Reply;
        _inFlight.Remove(request.SessionId);
        reply.SetResult(result);
    }

    private Task<AgentRunResult> Run(AgentRunRequest request)
    {
        if (AutoReply is not null)
        {
            return Task.FromResult(AutoReply(request));
        }

        var reply = new TaskCompletionSource<AgentRunResult>();
        _inFlight[request.SessionId] = (request, reply);
        return reply.Task;
    }
}
