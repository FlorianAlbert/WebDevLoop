using WebDevLoop.Core.Agents;
using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Orchestration.Integration;
using WebDevLoop.Core.Orchestration.Recovery.AgentSteps;
using WebDevLoop.Core.Ports;
using WebDevLoop.Core.Tests.Orchestration.Completion.Testing;
using WebDevLoop.Core.Tests.Orchestration.ReviewLoop;
using WebDevLoop.Core.Tests.Orchestration.TicketExecution;
using WebDevLoop.Core.Tests.Ports.Fakes;

namespace WebDevLoop.Core.Tests.Orchestration.Recovery.AgentSteps;

/// <summary>
/// Runs agent-step recovery against the CAS workflow database, test leases, a scripted test-target supervisor, a fake
/// Copilot runtime pool, and recording launchers. <see cref="Restart"/> simulates a new app process: everything started
/// before it belongs to the previous process.
/// </summary>
internal sealed class AgentStepRecoveryFixture
{
    public static readonly TimeSpan TokenLifetime = TimeSpan.FromHours(1);
    public static readonly TimeSpan RefreshSkew = TimeSpan.FromMinutes(5);
    public static readonly TimeSpan GracePeriod = TimeSpan.FromMinutes(5);
    public static readonly TimeSpan RuntimeIdleTimeout = TimeSpan.FromMinutes(10);

    public AgentStepRecoveryFixture()
    {
        Boot = ProcessBoot.Now(Clock);
        Runtimes = new JournalingRuntimePool(new FakeCopilotRuntimePool(Clock, TokenLifetime, RefreshSkew, RuntimeIdleTimeout), Journal);
    }

    public TestingFixture Testing { get; } = new();

    public TicketExecutionFixture Execution => Testing.Execution;

    public ReviewLoopFixture Review => Testing.Parent.Review;

    public CasWorkflowDatabase Db => Testing.Db;

    public FakeClock Clock => Execution.Clock;

    public RecordingIntegrationLauncher Integration { get; } = new();

    public RecordingAgentLogSink Logs { get; } = new();

    /// <summary>Order of runtime refreshes and agent turns.</summary>
    public List<string> Journal { get; } = [];

    public JournalingRuntimePool Runtimes { get; }

    public ProcessBoot Boot { get; private set; }

    public static CancellationToken Token => TicketExecutionFixture.Token;

    /// <summary>A new app process starts a minute later.</summary>
    public void Restart()
    {
        Clock.Advance(TimeSpan.FromMinutes(1));
        Boot = ProcessBoot.Now(Clock);
    }

    public AgentStepRecoveryService Service()
    {
        TestingWorkflowScope scope = Testing.OpenScope();
        CasWorkflowScope workflow = scope.Workflow;
        var launchers = new AgentStepRecoveryLaunchers(Execution.Launcher, Review.Launcher, Integration, Testing.Parent.Launcher, Testing.Launcher);
        var options = new AgentStepRecoveryOptions(GracePeriod);
        return new AgentStepRecoveryService(
            Runtimes,
            LeaseStopper(scope),
            new InterruptedStepFinisher(workflow, workflow, workflow, Execution.Settings, Review.Agents, Logs, workflow, scope, Clock, Boot, options),
            new StalledWorkRelauncher(workflow, workflow, workflow, launchers, Clock, Boot, options));
    }

    public OrphanedTestLeaseStopper LeaseStopper(TestingWorkflowScope? scope = null)
    {
        scope ??= Testing.OpenScope();
        return new OrphanedTestLeaseStopper(scope, Testing.Target, scope, Clock, Boot);
    }

    public Task<AgentStepRecoveryReport> RecoverAsync() => Service().RecoverAsync(Token);

    /// <summary>Commits a step that is running since now, as a runner of the current process would leave it.</summary>
    public async Task<StepRun> SeedRunningStepAsync(
        RunId specRunId, TicketRunId? ticketRunId, StepKind kind, AgentRole? role, string id, int attempt = 1, string? session = null)
    {
        StepRun step = StepRun.Create(new StepRunId(id), specRunId, ticketRunId, kind, role, attempt, "hash");
        step.CopilotSessionId = session ?? (role is null ? null : $"session-{id}");
        step.Start(Clock.UtcNow, TimeSpan.FromMinutes(30));
        await SaveAsync(step);
        return step;
    }

    /// <summary>Commits a step that an earlier recovery pass already finished as interrupted.</summary>
    public async Task SeedInterruptedStepAsync(RunId specRunId, TicketRunId? ticketRunId, StepKind kind, AgentRole role, string id, int attempt)
    {
        StepRun step = StepRun.Create(new StepRunId(id), specRunId, ticketRunId, kind, role, attempt, "hash");
        step.CopilotSessionId = $"session-{id}";
        step.Start(Clock.UtcNow, TimeSpan.FromMinutes(30));
        step.Finish(StepStatus.Failed, Clock.UtcNow, failureReason: StepInterruption.Restarted);
        await SaveAsync(step);
    }

    public TestLease SeedLease(RunId specRunId, int port, TimeSpan timeToLive) =>
        Testing.Leases.Seed(TestLease.Acquire(specRunId, port, $"/work/runs/{specRunId}/test", Clock.UtcNow, timeToLive));

    public StepRun Step(string id) => Db.Rows<StepRun>().Single(step => step.Id == new StepRunId(id));

    public TicketRun Ticket(TicketRunId id) => Execution.Ticket(id);

    public SpecRun Spec(RunId id) => Execution.Spec(id);

    private async Task SaveAsync(StepRun step)
    {
        CasWorkflowScope scope = Db.OpenScope();
        scope.Add(step);
        Assert.Equal(SaveOutcome.Saved, await scope.SaveChangesAsync(Token));
    }
}

internal sealed class RecordingIntegrationLauncher : IIntegrationLauncher
{
    public List<IntegrationAssignment> Launched { get; } = [];

    public void Launch(IntegrationAssignment assignment) => Launched.Add(assignment);
}

/// <summary>Journals refreshes of the wrapped pool, so tests can check they happen before any resumed agent turn.</summary>
internal sealed class JournalingRuntimePool(FakeCopilotRuntimePool inner, List<string> journal) : ICopilotRuntimePool
{
    public FakeCopilotRuntimePool Inner { get; } = inner;

    /// <summary>When set, refreshing fails with this exception (e.g. the token service is down).</summary>
    public Exception? RefreshFailure { get; set; }

    public Task<CopilotRuntimeLease> AcquireAsync(CopilotAuthIdentity identity, CancellationToken cancellationToken) =>
        Inner.AcquireAsync(identity, cancellationToken);

    public Task<CopilotRuntimeKey> ReplaceAsync(CopilotRuntimeKey stale, CancellationToken cancellationToken) =>
        Inner.ReplaceAsync(stale, cancellationToken);

    public async Task<IReadOnlyList<CopilotRuntimeKey>> RefreshExpiringAsync(CancellationToken cancellationToken)
    {
        if (RefreshFailure is not null)
        {
            throw RefreshFailure;
        }

        IReadOnlyList<CopilotRuntimeKey> refreshed = await Inner.RefreshExpiringAsync(cancellationToken);
        journal.Add($"refresh {refreshed.Count}");
        return refreshed;
    }

    public Task<IReadOnlyList<CopilotRuntimeKey>> EvictIdleAsync(CancellationToken cancellationToken) => Inner.EvictIdleAsync(cancellationToken);
}
