using WebDevLoop.Core.Orchestration.Recovery.Startup;
using WebDevLoop.Infrastructure.Prerequisites;
using WebDevLoop.Web.DependencyInjection;

namespace WebDevLoop.Web.Background;

/// <summary>
/// Drives <see cref="RecoveryCoordinator"/>: while the app is diagnostic-only it waits (re-checking every
/// <see cref="WorkflowWorkerOptions.StartupRetryInterval"/>, e.g. after the Health page's "Re-check" turned the readiness
/// operational); then it runs startup recovery once, which opens the <see cref="SchedulerStartGate"/> for the other
/// workers, and afterwards a periodic reconciliation cycle every <see cref="WorkflowWorkerOptions.RecoveryInterval"/>.
/// Every cycle runs in a DI scope of its own.
/// </summary>
public sealed partial class RecoveryWorker(
    IServiceScopeFactory scopes,
    DiagnosticReadiness readiness,
    SchedulerStartGate schedulers,
    WorkflowWorkerOptions options,
    ILogger<RecoveryWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        bool reportedDiagnosticMode = false;
        while (!stoppingToken.IsCancellationRequested)
        {
            if (schedulers.IsOpen)
            {
                await RunCycleAsync((coordinator, token) => coordinator.RunPeriodicAsync(readiness.Current.Report, token), stoppingToken);
            }
            else if (readiness.Current.Mode == ReadinessMode.Operational)
            {
                RecoveryCycleReport? report = await RunCycleAsync((coordinator, token) => coordinator.RunStartupAsync(readiness.Current.Report, token), stoppingToken);
                if (report is { SchedulersStarted: true })
                {
                    LogWorkflowStarted(logger);
                }
            }
            else if (!reportedDiagnosticMode)
            {
                reportedDiagnosticMode = true;
                LogWaitingForPrerequisites(logger);
            }

            await Task.Delay(schedulers.IsOpen ? options.RecoveryInterval : options.StartupRetryInterval, stoppingToken);
        }
    }

    private async Task<RecoveryCycleReport?> RunCycleAsync(
        Func<RecoveryCoordinator, CancellationToken, Task<RecoveryCycleReport>> cycle,
        CancellationToken stoppingToken)
    {
        try
        {
            await using AsyncServiceScope scope = scopes.CreateAsyncScope();
            RecoveryCycleReport report = await cycle(scope.ServiceProvider.GetRequiredService<RecoveryCoordinator>(), stoppingToken);
            foreach (RecoveryStageFault fault in report.Faults)
            {
                LogStageFault(logger, report.Kind, fault.Stage, fault.Error);
            }

            return report;
        }
        catch (Exception exception) when (!stoppingToken.IsCancellationRequested)
        {
            // Includes timeouts (TaskCanceledException from HttpClient): only shutdown may end the worker.
            LogCycleFailed(logger, exception);
            return null;
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Startup recovery finished; the workflow schedulers are running.")]
    private static partial void LogWorkflowStarted(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Prerequisites are not met: the app runs diagnostic-only and the workflow does not start until the Health page reports Operational.")]
    private static partial void LogWaitingForPrerequisites(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "{Kind} recovery stage {Stage} failed: {Error}")]
    private static partial void LogStageFault(ILogger logger, RecoveryCycleKind kind, RecoveryStage stage, string error);

    [LoggerMessage(Level = LogLevel.Error, Message = "A recovery cycle failed; the next cycle retries.")]
    private static partial void LogCycleFailed(ILogger logger, Exception exception);
}
