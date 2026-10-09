using WebDevLoop.Core.Domain;

namespace WebDevLoop.Core.Ports;

public interface IStepRunRepository
{
    Task<StepRun?> GetAsync(StepRunId id, CancellationToken cancellationToken);

    Task<IReadOnlyList<StepRun>> ListBySpecRunAsync(RunId specRunId, CancellationToken cancellationToken);

    Task<IReadOnlyList<StepRun>> ListByTicketRunAsync(TicketRunId ticketRunId, CancellationToken cancellationToken);

    /// <summary>Pending or running steps across all runs (recovery).</summary>
    Task<IReadOnlyList<StepRun>> ListActiveAsync(CancellationToken cancellationToken);

    void Add(StepRun stepRun);
}
