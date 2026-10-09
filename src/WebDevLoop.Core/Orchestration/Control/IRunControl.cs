using WebDevLoop.Core.Domain;

namespace WebDevLoop.Core.Orchestration.Control;

/// <summary>
/// The user's Retry/Skip/Abort commands (UI and API). Every command is compare-and-swap safe, audited as a run event, and
/// emits outbox events; a lost race changes nothing and reports <see cref="ControlOutcome.ConcurrencyConflict"/>.
/// </summary>
public interface IRunControl
{
    /// <summary>Resumes a spec in <c>NeedsAttention</c> at its failed phase, claiming a free active-spec slot when that phase is active.</summary>
    Task<ControlResult> RetrySpecAsync(RunId specRunId, CancellationToken cancellationToken);

    /// <summary>Aborts a non-terminal spec: its open tickets and active steps are aborted, agent sessions and the tester app are stopped, and its slot is released.</summary>
    Task<ControlResult> AbortSpecAsync(RunId specRunId, CancellationToken cancellationToken);

    /// <summary>Resumes a ticket in <c>NeedsAttention</c>: implement again, review again, or resume its integration.</summary>
    Task<ControlResult> RetryTicketAsync(TicketRunId ticketRunId, CancellationToken cancellationToken);

    /// <summary>Gives up a ticket that is not being worked on (<c>Blocked</c>, <c>Ready</c>, <c>NeedsAttention</c>).</summary>
    Task<ControlResult> SkipTicketAsync(TicketRunId ticketRunId, SkipDependents dependents, CancellationToken cancellationToken);

    /// <summary>Stops a non-terminal ticket: its active steps are cancelled and their agent sessions aborted. Its dependents stay blocked.</summary>
    Task<ControlResult> AbortTicketAsync(TicketRunId ticketRunId, CancellationToken cancellationToken);
}
