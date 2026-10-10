using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Queries;
using WebDevLoop.Web.Components.Tickets;

namespace WebDevLoop.Web.Components.Runs;

/// <summary>Builds the control commands of a spec run; the abort command says how many open tickets it aborts.</summary>
public static class SpecRunCommands
{
    private const int MaxNamedTickets = 6;

    private static readonly AttentionActionKind[] AllKinds = [AttentionActionKind.Retry, AttentionActionKind.Abort];

    public static IReadOnlyList<RunControlCommand> Build(SpecRunView spec, IReadOnlyList<TicketRunView> tickets, AttentionReason? reason = null)
    {
        IEnumerable<AttentionActionKind> kinds = reason is null
            ? AllKinds
            : reason.Actions.Select(action => action.Kind).Where(kind => kind is AttentionActionKind.Retry or AttentionActionKind.Abort).Distinct();
        return kinds.Select(kind => Build(kind, reason?.Actions.FirstOrDefault(action => action.Kind == kind), spec, tickets)).ToList();
    }

    /// <summary>"3 open tickets (#4 Title, #5 Title, #6 Title)", or "no open tickets".</summary>
    public static string OpenTickets(IReadOnlyList<TicketRunView> tickets)
    {
        TicketRunView[] open = tickets.Where(ticket => !ticket.Status.IsTerminal()).OrderBy(ticket => ticket.IssueNumber).ToArray();
        if (open.Length == 0)
        {
            return "no open tickets";
        }

        string named = TicketDependents.Describe(open.Take(MaxNamedTickets));
        string more = open.Length > MaxNamedTickets ? $" and {open.Length - MaxNamedTickets} more" : string.Empty;
        return $"{open.Length} open ticket{(open.Length == 1 ? "" : "s")} ({named}{more})";
    }

    private static RunControlCommand Build(AttentionActionKind kind, AttentionAction? action, SpecRunView spec, IReadOnlyList<TicketRunView> tickets)
    {
        var id = new RunId(spec.Id);
        if (kind == AttentionActionKind.Retry)
        {
            return new RunControlCommand(
                "retry", action?.Label ?? "Retry", spec.Status == SpecRunStatus.NeedsAttention, "Retry is only available when the run needs attention.", null,
                (control, token) => control.RetrySpecAsync(id, token),
                action?.Consequence ?? "Resumes the run where it stopped. Nothing is lost.");
        }

        string open = OpenTickets(tickets);
        int openCount = tickets.Count(ticket => !ticket.Status.IsTerminal());
        return new RunControlCommand(
            "abort", action?.Label ?? "Abort run", !spec.Status.IsTerminal(), "This run has already finished.",
            $"Abort this spec run? It aborts {open}, stops its agent sessions and tester app, and cleans up its worktrees. This cannot be undone.",
            (control, token) => control.AbortSpecAsync(id, token),
            action?.Consequence ?? "Cancels the whole run and all of its unfinished tickets. Anything already pushed to GitHub stays there; nothing is deleted.",
            openCount == 0 ? "No tickets are open." : $"Aborts {open}.");
    }
}
