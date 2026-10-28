using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Queries;

namespace WebDevLoop.Web.Components.Tickets;

/// <summary>
/// Which other tickets a control command on a ticket touches, computed from the spec's ticket list so the page can name them
/// before the user confirms an abort.
/// </summary>
public static class TicketDependents
{
    private const int MaxTitleLength = 60;

    /// <summary>Tickets that list <paramref name="ticket"/> as a blocker.</summary>
    public static IReadOnlyList<TicketRunView> Direct(TicketRunView ticket, IReadOnlyList<TicketRunView> all) =>
        all.Where(other => other.Id != ticket.Id && other.BlockedByTicketRunIds.Contains(ticket.Id)).OrderBy(other => other.IssueNumber).ToList();

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

}
