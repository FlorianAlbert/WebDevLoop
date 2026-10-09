using WebDevLoop.Core.Domain;

namespace WebDevLoop.Core.Orchestration.Control;

public sealed class RunControlService(SpecRunControl specs, TicketRunControl tickets) : IRunControl
{
    public Task<ControlResult> RetrySpecAsync(RunId specRunId, CancellationToken cancellationToken) => specs.RetryAsync(specRunId, cancellationToken);

    public Task<ControlResult> AbortSpecAsync(RunId specRunId, CancellationToken cancellationToken) => specs.AbortAsync(specRunId, cancellationToken);

    public Task<ControlResult> RetryTicketAsync(TicketRunId ticketRunId, CancellationToken cancellationToken) => tickets.RetryAsync(ticketRunId, cancellationToken);

    public Task<ControlResult> SkipTicketAsync(TicketRunId ticketRunId, SkipDependents dependents, CancellationToken cancellationToken) =>
        tickets.SkipAsync(ticketRunId, dependents, cancellationToken);

    public Task<ControlResult> AbortTicketAsync(TicketRunId ticketRunId, CancellationToken cancellationToken) => tickets.AbortAsync(ticketRunId, cancellationToken);
}
