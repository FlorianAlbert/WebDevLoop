using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using WebDevLoop.Core.Orchestration.Recovery.AgentSteps;
using WebDevLoop.Core.Orchestration.Recovery.ExternalState;
using WebDevLoop.Core.Orchestration.Recovery.Startup;
using WebDevLoop.Infrastructure.Prerequisites;
using WebDevLoop.Web.Background;
using WebDevLoop.Web.DependencyInjection;
using WebDevLoop.Web.Tests.Api;

namespace WebDevLoop.Web.Tests.Background;

/// <summary>A timeout (an <see cref="OperationCanceledException"/> not caused by shutdown) must not stop a worker or the host.</summary>
public sealed class WorkerResilienceTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);
    private static readonly WorkflowWorkerOptions FastTimers = new()
    {
        StartupRetryInterval = TimeSpan.FromMilliseconds(20),
        RecoveryInterval = TimeSpan.FromMilliseconds(20),
    };

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task a_periodic_pass_timing_out_is_retried_on_the_next_tick()
    {
        var schedulers = new SchedulerStartGate();
        schedulers.Open();
        await using ServiceProvider services = new ServiceCollection().BuildServiceProvider();
        var worker = new TimingOutOnceWorker(services.GetRequiredService<IServiceScopeFactory>(), schedulers);

        await worker.StartAsync(Ct);
        await worker.SecondPass.Task.WaitAsync(Timeout, Ct);
        await worker.StopAsync(Ct);
    }

    [Fact]
    public async Task a_recovery_stage_timing_out_does_not_stop_recovery_from_starting_the_workflow()
    {
        var schedulers = new SchedulerStartGate();
        var readiness = new DiagnosticReadiness(new FakePrerequisiteValidator(), new FakeClock());
        await readiness.RefreshAsync(Ct);
        await using ServiceProvider services = new ServiceCollection()
            .AddSingleton<IExternalStateRecovery, TimingOutOnceExternalState>()
            .AddSingleton<IAgentStepRecovery, FailingStages>()
            .AddSingleton<IOutboxReplay, FailingStages>()
            .AddSingleton<ISpecQueueRecomputation, FailingStages>()
            .AddSingleton<IFrontierReconciliationTrigger, FailingStages>()
            .AddSingleton(schedulers)
            .AddScoped<RecoveryCoordinator>()
            .BuildServiceProvider();
        var worker = new RecoveryWorker(services.GetRequiredService<IServiceScopeFactory>(), readiness, schedulers, FastTimers, NullLogger<RecoveryWorker>.Instance);

        await worker.StartAsync(Ct);
        await schedulers.WaitUntilOpenAsync(Ct).WaitAsync(Timeout, Ct);
        await worker.StopAsync(Ct);
    }

    private sealed class TimingOutOnceWorker(IServiceScopeFactory scopes, SchedulerStartGate schedulers)
        : GatedPeriodicWorker(scopes, schedulers, NullLogger.Instance)
    {
        private int _passes;

        public TaskCompletionSource SecondPass { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        protected override TimeSpan Interval => TimeSpan.FromMilliseconds(20);

        protected override Task<bool> RunPassAsync(IServiceProvider services, CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref _passes) == 1)
            {
                throw new TaskCanceledException("HTTP request timed out.");
            }

            SecondPass.TrySetResult();
            return Task.FromResult(false);
        }
    }

    private sealed class TimingOutOnceExternalState : IExternalStateRecovery
    {
        private int _calls;

        public Task<ExternalReconciliationReport> ReconcileAsync(CancellationToken cancellationToken) =>
            Interlocked.Increment(ref _calls) == 1
                ? throw new TaskCanceledException("GitHub did not answer in time.")
                : throw new InvalidOperationException("Recorded as a stage fault.");
    }

    /// <summary>Every other stage fails normally, which the coordinator records as a fault and continues.</summary>
    private sealed class FailingStages : IAgentStepRecovery, IOutboxReplay, ISpecQueueRecomputation, IFrontierReconciliationTrigger
    {
        public Task<AgentStepRecoveryReport> RecoverAsync(CancellationToken cancellationToken) => throw new InvalidOperationException("stage fault");

        public Task<int> ReplayPendingAsync(CancellationToken cancellationToken) => Task.FromResult(0);

        public Task<QueueRecomputationReport> RecomputeAsync(CancellationToken cancellationToken) => throw new InvalidOperationException("stage fault");

        public Task<int> RaiseAsync(CancellationToken cancellationToken) => Task.FromResult(0);
    }
}
