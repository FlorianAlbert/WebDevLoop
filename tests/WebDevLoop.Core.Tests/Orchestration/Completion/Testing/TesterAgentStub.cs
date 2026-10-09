using WebDevLoop.Core.Agents;
using WebDevLoop.Core.Ports;
using WebDevLoop.Core.Orchestration.Results;

namespace WebDevLoop.Core.Tests.Orchestration.Completion.Testing;

/// <summary>Plays scripted tester turns; a turn may report, fail, throw, or hang until it is cancelled or aborted.</summary>
internal sealed class TesterAgentStub : IAgentRunner
{
    private readonly Queue<Func<AgentRunRequest, CancellationToken, Task<AgentRunResult>>> _turns = [];

    public List<AgentRunRequest> Started { get; } = [];

    public List<AgentSessionId> Aborted { get; } = [];

    /// <summary>Runs inside every turn before it ends, e.g. to observe the lease while the tester is running.</summary>
    public Action<AgentRunRequest>? DuringTurn { get; set; }

    public TesterAgentStub Reports(TestReport report) => Turn((_, _) => Task.FromResult(AgentRunResult.Reported(report)));

    public TesterAgentStub Ends(AgentRunOutcome outcome, string reason) => Turn((_, _) => Task.FromResult(AgentRunResult.NotReported(outcome, reason)));

    public TesterAgentStub Throws(Exception exception) => Turn((_, _) => Task.FromException<AgentRunResult>(exception));

    /// <summary>A turn that only ends when cancelled, returning <see cref="AgentRunOutcome.Cancelled"/> like the real runner.</summary>
    public TesterAgentStub Hangs() => Turn(async (_, cancellationToken) =>
    {
        try
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
        }
        catch (OperationCanceledException)
        {
        }

        return AgentRunResult.NotReported(AgentRunOutcome.Cancelled, "The agent step was cancelled.");
    });

    public TesterAgentStub Turn(Func<AgentRunRequest, CancellationToken, Task<AgentRunResult>> turn)
    {
        _turns.Enqueue(turn);
        return this;
    }

    public async Task<AgentRunResult> StartAsync(AgentRunRequest request, CancellationToken cancellationToken)
    {
        Started.Add(request);
        DuringTurn?.Invoke(request);
        return _turns.Count == 0
            ? AgentRunResult.NotReported(AgentRunOutcome.MissingReport, "No scripted tester turn.")
            : await _turns.Dequeue()(request, cancellationToken);
    }

    public Task<AgentRunResult> ResumeAsync(AgentRunRequest request, CancellationToken cancellationToken) =>
        throw new NotSupportedException("Tester turns always start a fresh session.");

    public Task AbortAsync(AgentSessionId sessionId, CancellationToken cancellationToken)
    {
        Aborted.Add(sessionId);
        return Task.CompletedTask;
    }
}
