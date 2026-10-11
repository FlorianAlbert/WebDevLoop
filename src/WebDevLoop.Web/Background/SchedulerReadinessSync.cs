using WebDevLoop.Core.Orchestration.Recovery.Startup;
using WebDevLoop.Infrastructure.Prerequisites;

namespace WebDevLoop.Web.Background;

/// <summary>Keeps scheduler admission in sync with every prerequisite evaluation, including manual re-checks.</summary>
public sealed class SchedulerReadinessSync(DiagnosticReadiness readiness, SchedulerStartGate schedulers) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        readiness.Changed += OnChanged;
        OnChanged(readiness.Current);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        readiness.Changed -= OnChanged;
        return Task.CompletedTask;
    }

    private void OnChanged(ReadinessSnapshot snapshot) =>
        schedulers.SetPrerequisitesReady(snapshot.Mode == ReadinessMode.Operational);
}
