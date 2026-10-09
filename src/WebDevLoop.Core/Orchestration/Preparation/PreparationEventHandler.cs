using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Events;
using WebDevLoop.Core.Ports;

namespace WebDevLoop.Core.Orchestration.Preparation;

/// <summary>
/// Event-bus subscriber for workflow steps 1–3: a spec the queue claimed (entering <c>Preparing</c>) is prepared in the
/// background (<see cref="IPreparationLauncher"/>). A (periodic or startup) <see cref="FrontierReconciliationRequested"/>
/// relaunches a <c>Preparing</c> spec without an active step, e.g. after a crash during the clone or the snapshot, or after
/// recovery finished an interrupted exploration (<c>SpecsAwaitingPreparation</c>); preparation is replay-safe.
/// </summary>
public sealed class PreparationEventHandler(IPreparationLauncher launcher, ISpecRunRepository specRuns, IStepRunRepository stepRuns)
{
    public async Task HandleAsync(EventEnvelope envelope, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        switch (envelope.Event)
        {
            case SpecRunStatusChanged { To: SpecRunStatus.Preparing } claimed:
                launcher.Launch(new PreparationAssignment(claimed.SpecRunId));
                break;
            case FrontierReconciliationRequested requested when await IsIdlePreparationAsync(requested.SpecRunId, cancellationToken):
                launcher.Launch(new PreparationAssignment(requested.SpecRunId));
                break;
        }
    }

    private async Task<bool> IsIdlePreparationAsync(RunId specRunId, CancellationToken cancellationToken) =>
        await specRuns.GetAsync(specRunId, cancellationToken) is { Status: SpecRunStatus.Preparing }
        && !(await stepRuns.ListBySpecRunAsync(specRunId, cancellationToken)).Any(step => step.IsActive);
}
