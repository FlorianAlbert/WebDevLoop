using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Queries;

namespace WebDevLoop.Web.Components.Tickets;

/// <summary>
/// Which other tickets a control command on a ticket touches, computed from the spec's ticket list so the page can name them
/// before the user confirms. <see cref="ToSkip"/> mirrors the dependents the control service skips for "Skip with dependents".
/// </summary>
public static class TicketDependents
{
    private const int MaxTitleLength = 60;

    /// <summary>Tickets that list <paramref name="ticket"/> as a blocker.</summary>
    public static IReadOnlyList<TicketRunView> Direct(TicketRunView ticket, IReadOnlyList<TicketRunView> all) =>
        all.Where(other => other.Id != ticket.Id && other.BlockedByTicketRunIds.Contains(ticket.Id)).OrderBy(other => other.IssueNumber).ToList();

    /// <summary>Direct dependents that wait for <paramref name="ticket"/> now and are released when it is skipped.</summary>
    public static IReadOnlyList<TicketRunView> Released(TicketRunView ticket, IReadOnlyList<TicketRunView> all) =>
        Direct(ticket, all).Where(other => other.Status == TicketRunStatus.Blocked).ToList();

    /// <summary>
    /// Tickets that (transitively) depend on <paramref name="ticket"/> and are not being worked on (blocked, ready or needing
    /// attention). The search does not continue past a dependent that is already running or finished, like the control service.
    /// </summary>
    public static IReadOnlyList<TicketRunView> ToSkip(TicketRunView ticket, IReadOnlyList<TicketRunView> all)
    {
        List<TicketRunView> found = [];
        HashSet<string> visited = [ticket.Id];
        Queue<TicketRunView> pending = new([ticket]);
        while (pending.TryDequeue(out TicketRunView? current))
        {
            foreach (TicketRunView dependent in Direct(current, all))
            {
                if (visited.Add(dependent.Id) && IsSkippable(dependent))
                {
                    found.Add(dependent);
                    pending.Enqueue(dependent);
                }
            }
        }

        return found.OrderBy(other => other.IssueNumber).ToList();
    }

    /// <summary>Blocked tickets that wait for <paramref name="ticket"/> directly or through other blocked tickets; aborting it leaves them waiting.</summary>
    public static IReadOnlyList<TicketRunView> StayBlocked(TicketRunView ticket, IReadOnlyList<TicketRunView> all)
    {
        List<TicketRunView> found = [];
        HashSet<string> visited = [ticket.Id];
        Queue<TicketRunView> pending = new([ticket]);
        while (pending.TryDequeue(out TicketRunView? current))
        {
            foreach (TicketRunView dependent in Direct(current, all))
            {
                if (dependent.Status == TicketRunStatus.Blocked && visited.Add(dependent.Id))
                {
                    pending.Enqueue(dependent);
                    found.Add(dependent);
                }
            }
        }

        return found.OrderBy(other => other.IssueNumber).ToList();
    }

    /// <summary>"#4 Add login, #5 Add logout" (titles shortened).</summary>
    public static string Describe(IEnumerable<TicketRunView> tickets) => string.Join(", ", tickets.Select(Describe));

    public static string Describe(TicketRunView ticket) =>
        ticket.Title.Length <= MaxTitleLength ? $"#{ticket.IssueNumber} {ticket.Title}" : $"#{ticket.IssueNumber} {ticket.Title[..(MaxTitleLength - 1)]}…";

    private static bool IsSkippable(TicketRunView ticket) =>
        ticket.Status is TicketRunStatus.Blocked or TicketRunStatus.Ready or TicketRunStatus.NeedsAttention;
}
