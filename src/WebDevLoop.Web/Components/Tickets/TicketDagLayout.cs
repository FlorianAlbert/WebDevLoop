using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Queries;

namespace WebDevLoop.Web.Components.Tickets;

/// <summary>Layers a spec's ticket DAG and flags the frontier, mirroring the orchestrator's rule that only integrated blockers count.</summary>
public static class TicketDagLayout
{
    public static IReadOnlyList<TicketDagRow> Build(IReadOnlyList<TicketRunView> tickets)
    {
        ArgumentNullException.ThrowIfNull(tickets);

        Dictionary<string, TicketRunView> byId = tickets.ToDictionary(ticket => ticket.Id);
        Dictionary<string, int> layers = [];

        return tickets
            .Select(ticket =>
            {
                TicketBlocker[] blockers = ticket.BlockedByTicketRunIds
                    .Select(id => byId.TryGetValue(id, out TicketRunView? blocker)
                        ? new TicketBlocker(id, blocker.IssueNumber, blocker.Status)
                        : new TicketBlocker(id, null, null))
                    .ToArray();
                bool awaitingStart = ticket.Status is TicketRunStatus.Blocked or TicketRunStatus.Ready;
                return new TicketDagRow(ticket, LayerOf(ticket.Id, byId, layers, []), awaitingStart && blockers.All(blocker => blocker.IsSatisfied), blockers);
            })
            .OrderBy(row => row.Layer)
            .ThenBy(row => row.Ticket.IssueNumber)
            .ToArray();
    }

    private static int LayerOf(string id, Dictionary<string, TicketRunView> byId, Dictionary<string, int> layers, HashSet<string> path)
    {
        if (layers.TryGetValue(id, out int known))
        {
            return known;
        }

        // A cycle is malformed data; the edge that closes it contributes no depth so the layout still terminates.
        if (!path.Add(id))
        {
            return -1;
        }

        int layer = byId[id].BlockedByTicketRunIds
            .Where(byId.ContainsKey)
            .Select(blocker => LayerOf(blocker, byId, layers, path) + 1)
            .DefaultIfEmpty(0)
            .Max();
        path.Remove(id);
        layers[id] = layer;
        return layer;
    }
}
