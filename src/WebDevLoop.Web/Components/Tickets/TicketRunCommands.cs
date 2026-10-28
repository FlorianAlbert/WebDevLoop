using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Orchestration.Control;
using WebDevLoop.Core.Queries;
using WebDevLoop.Web.Components.Runs;

namespace WebDevLoop.Web.Components.Tickets;

/// <summary>
/// Builds the control commands of a ticket with a consequence line each and confirmations that name the affected tickets.
/// With an <see cref="AttentionReason"/> only the buttons it lists are offered, worded as it words them.
/// </summary>
public static class TicketRunCommands
{
    private static readonly AttentionActionKind[] AllKinds =
        [AttentionActionKind.Retry, AttentionActionKind.Abort];

    public static IReadOnlyList<RunControlCommand> Build(TicketRunView ticket, IReadOnlyList<TicketRunView> siblings, AttentionReason? reason = null)
    {
        IEnumerable<AttentionActionKind> kinds = reason is null ? AllKinds : reason.Actions.Select(action => action.Kind).Where(AllKinds.Contains).Distinct();
        return kinds.Select(kind => Build(kind, reason?.Actions.FirstOrDefault(action => action.Kind == kind), ticket, siblings)).ToList();
    }

    private static RunControlCommand Build(AttentionActionKind kind, AttentionAction? action, TicketRunView ticket, IReadOnlyList<TicketRunView> siblings)
    {
        var id = new TicketRunId(ticket.Id);
        string name = TicketDependents.Describe(ticket);
        switch (kind)
        {
            case AttentionActionKind.Retry:
                return new RunControlCommand(
                    "retry", action?.Label ?? "Retry", ticket.Status == TicketRunStatus.NeedsAttention, "Retry is only available when the ticket needs attention.", null,
                    (control, token) => control.RetryTicketAsync(id, token),
                    action?.Consequence ?? "Resumes the phase that stopped. Nothing is lost.");

            default:
                IReadOnlyList<TicketRunView> blocked = TicketDependents.StayBlocked(ticket, siblings);
                return new RunControlCommand(
                    "abort", action?.Label ?? "Abort ticket", !ticket.Status.IsTerminal(), "This ticket has already finished.",
                    $"Abort {name}? Its agent session is stopped. {(blocked.Count == 0 ? "No other ticket is waiting for it." : $"{TicketDependents.Describe(blocked)} still require their GitHub blockers to close.")}",
                    (control, token) => control.AbortTicketAsync(id, token),
                    action?.Consequence ?? "Stops this ticket for good. Dependents still require every native GitHub blocker to close.",
                    blocked.Count == 0 ? "No other ticket is waiting for this one." : $"Stay blocked: {TicketDependents.Describe(blocked)}.");
        }
    }
}
