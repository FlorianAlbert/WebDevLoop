using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Orchestration.Preparation;

namespace WebDevLoop.Core.Orchestration.TicketExecution;

/// <summary>Ticket worktrees live in the run's app-owned directory: <c>&lt;root&gt;/runs/&lt;run-id&gt;/tickets/&lt;ticket-id&gt;</c>.</summary>
public static class TicketWorktreeLayout
{
    private const string TicketsDirectoryName = "tickets";

    public static string PathFor(string workspaceRoot, RunId runId, TicketRunId ticketId) =>
        Path.Combine(RunWorkspaceLayout.For(workspaceRoot, runId).RunDirectory, TicketsDirectoryName, ticketId.Value);
}
