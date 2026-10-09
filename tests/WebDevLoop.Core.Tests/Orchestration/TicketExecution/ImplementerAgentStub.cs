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

    /// <summary>When set, every started turn finishes immediately with this result.</summary>
    public Func<AgentRunRequest, AgentRunResult>? AutoReply { get; set; }

    public IEnumerable<AgentRunRequest> InFlight => _inFlight.Values.Select(entry => entry.Request);

    public Task<AgentRunResult> StartAsync(AgentRunRequest request, CancellationToken cancellationToken)
    {
        Started.Add(request);
        if (AutoReply is not null)
        {
            return Task.FromResult(AutoReply(request));
        }

        var reply = new TaskCompletionSource<AgentRunResult>();
        _inFlight[request.SessionId] = (request, reply);
        return reply.Task;
    }

    public Task<AgentRunResult> ResumeAsync(AgentRunRequest request, CancellationToken cancellationToken) =>
        throw new NotSupportedException("Implementation dispatch never resumes sessions.");

    public Task AbortAsync(AgentSessionId sessionId, CancellationToken cancellationToken) => Task.CompletedTask;

    public AgentRunRequest InFlightIn(string worktreePath) =>
        InFlight.Single(request => request.Policy.Paths.WorkingDirectory == worktreePath);

    public void Reply(AgentRunRequest request, AgentRunResult result)
    {
        TaskCompletionSource<AgentRunResult> reply = _inFlight[request.SessionId].Reply;
        _inFlight.Remove(request.SessionId);
        reply.SetResult(result);
    }
}
