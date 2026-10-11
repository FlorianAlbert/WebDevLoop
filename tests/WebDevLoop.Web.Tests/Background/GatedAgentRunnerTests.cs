using WebDevLoop.Core.Agents;
using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Orchestration.Recovery.Startup;
using WebDevLoop.Core.Ports;
using WebDevLoop.Web.Background;

namespace WebDevLoop.Web.Tests.Background;

public sealed class GatedAgentRunnerTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);
    private static readonly AgentRunRequest Request = new(
        new StepRunId("step"), new AgentSessionId("session"), new GitHubRepoRef("acme", "widgets"),
        new AgentModelSettings("model", "high", TimeSpan.FromMinutes(1)), "Implement the ticket.",
        RoleCapabilityPolicies.For(AgentRole.Implementer, new AgentWorkspace(Path.GetFullPath("worktree"))));

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task new_and_resumed_turns_wait_while_paused_but_admitted_turns_finish(bool resume)
    {
        var gate = new SchedulerStartGate();
        var copilot = new ControlledAgentRunner();
        IAgentRunner agents = new GatedAgentRunner(copilot, gate);
        gate.Open();
        gate.SetPrerequisitesReady(false);

        Task<AgentRunResult> turn = resume ? agents.ResumeAsync(Request, Ct) : agents.StartAsync(Request, Ct);
        await Task.Delay(TimeSpan.FromMilliseconds(100), Ct);
        Assert.False(copilot.Launched.Task.IsCompleted);
        gate.SetPrerequisitesReady(true);
        await copilot.Launched.Task.WaitAsync(Timeout, Ct);

        gate.SetPrerequisitesReady(false);
        copilot.Finish.TrySetResult(AgentRunResult.NotReported(AgentRunOutcome.MissingReport, "Finished without a report."));
        Assert.Equal(AgentRunOutcome.MissingReport, (await turn.WaitAsync(Timeout, Ct)).Outcome);
    }

    [Fact]
    public async Task a_paused_launch_can_be_cancelled_and_aborts_do_not_wait_on_the_gate()
    {
        var gate = new SchedulerStartGate();
        var copilot = new ControlledAgentRunner();
        IAgentRunner agents = new GatedAgentRunner(copilot, gate);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(Ct);

        Task<AgentRunResult> turn = agents.StartAsync(Request, cancellation.Token);
        await agents.AbortAsync(Request.SessionId, Ct).WaitAsync(Timeout, Ct);
        Assert.True(copilot.Aborted);
        await cancellation.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => turn);
        gate.Open();
        Assert.False(copilot.Launched.Task.IsCompleted);
    }

    private sealed class ControlledAgentRunner : IAgentRunner
    {
        public TaskCompletionSource Launched { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<AgentRunResult> Finish { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool Aborted { get; private set; }

        public Task<AgentRunResult> StartAsync(AgentRunRequest request, CancellationToken cancellationToken)
        {
            Launched.TrySetResult();
            return Finish.Task.WaitAsync(cancellationToken);
        }

        public Task<AgentRunResult> ResumeAsync(AgentRunRequest request, CancellationToken cancellationToken) =>
            StartAsync(request, cancellationToken);

        public Task AbortAsync(AgentSessionId sessionId, CancellationToken cancellationToken)
        {
            Aborted = true;
            return Task.CompletedTask;
        }
    }
}
