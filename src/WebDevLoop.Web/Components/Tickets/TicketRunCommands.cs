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
    private const string SkipUnavailable = "Skipping is only available while the ticket is blocked, ready or needs attention.";

    private static readonly AttentionActionKind[] AllKinds =
        [AttentionActionKind.Retry, AttentionActionKind.Skip, AttentionActionKind.SkipWithDependents, AttentionActionKind.Abort];

    public static IReadOnlyList<RunControlCommand> Build(TicketRunView ticket, IReadOnlyList<TicketRunView> siblings, AttentionReason? reason = null)
    {
        IEnumerable<AttentionActionKind> kinds = reason is null ? AllKinds : reason.Actions.Select(action => action.Kind).Distinct();
        return kinds.Select(kind => Build(kind, reason?.Actions.FirstOrDefault(action => action.Kind == kind), ticket, siblings)).ToList();
    }

    private static RunControlCommand Build(AttentionActionKind kind, AttentionAction? action, TicketRunView ticket, IReadOnlyList<TicketRunView> siblings)
    {
        var id = new TicketRunId(ticket.Id);
        string name = TicketDependents.Describe(ticket);
        bool skippable = ticket.Status is TicketRunStatus.Blocked or TicketRunStatus.Ready or TicketRunStatus.NeedsAttention;
        switch (kind)
        {
            case AttentionActionKind.Retry:
                return new RunControlCommand(
                    "retry", action?.Label ?? "Retry", ticket.Status == TicketRunStatus.NeedsAttention, "Retry is only available when the ticket needs attention.", null,
                    (control, token) => control.RetryTicketAsync(id, token),
                    action?.Consequence ?? "Resumes the phase that stopped. Nothing is lost.");

            case AttentionActionKind.Skip:
                IReadOnlyList<TicketRunView> released = TicketDependents.Released(ticket, siblings);
                string unblocks = released.Count == 0
                    ? "No other ticket is waiting for this one."
                    : $"Unblocks {TicketDependents.Describe(released)}.";
                return new RunControlCommand(
                    "skip", action?.Label ?? "Skip", skippable, SkipUnavailable,
                    $"Skip {name}? It counts as done without its change. {(released.Count == 0 ? "No other ticket is waiting for it." : $"{TicketDependents.Describe(released)} will be unblocked and may start without its change.")}",
                    (control, token) => control.SkipTicketAsync(id, SkipDependents.Unblock, token),
                    action?.Consequence ?? "Gives up on this ticket without its change. Tickets that depend on it are released and continue without it.",
                    unblocks);

            case AttentionActionKind.SkipWithDependents:
                IReadOnlyList<TicketRunView> dependents = TicketDependents.ToSkip(ticket, siblings);
                string also = dependents.Count == 0
                    ? "No other tickets depend on this one."
                    : $"Also skips {TicketDependents.Describe(dependents)}.";
                return new RunControlCommand(
                    "skip-dependents", action?.Label ?? "Skip with dependents", skippable, SkipUnavailable,
                    dependents.Count == 0
                        ? $"Skip {name}? No other tickets depend on it."
                        : $"Skip {name} and the {dependents.Count} ticket{(dependents.Count == 1 ? "" : "s")} that depend{(dependents.Count == 1 ? "s" : "")} on it: {TicketDependents.Describe(dependents)}? None of them will be implemented.",
                    (control, token) => control.SkipTicketAsync(id, SkipDependents.Skip, token),
                    action?.Consequence ?? "Gives up on this ticket and on every ticket that depends on it. The rest of the spec carries on.",
                    also);

            default:
                IReadOnlyList<TicketRunView> blocked = TicketDependents.StayBlocked(ticket, siblings);
                return new RunControlCommand(
                    "abort", action?.Label ?? "Abort ticket", !ticket.Status.IsTerminal(), "This ticket has already finished.",
                    $"Abort {name}? Its agent session is stopped. {(blocked.Count == 0 ? "No other ticket is waiting for it." : $"{TicketDependents.Describe(blocked)} stay blocked until they are skipped.")}",
                    (control, token) => control.AbortTicketAsync(id, token),
                    action?.Consequence ?? "Stops this ticket for good. Tickets that depend on it stay blocked until you skip them.",
                    blocked.Count == 0 ? "No other ticket is waiting for this one." : $"Stay blocked: {TicketDependents.Describe(blocked)}.");
        }
    }
}
