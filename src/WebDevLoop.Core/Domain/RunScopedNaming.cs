namespace WebDevLoop.Core.Domain;

/// <summary>Branch names embed the run id so repeated runs for one issue never move each other's refs.</summary>
public static class RunScopedNaming
{
    public static BranchName IntegrationBranch(RunId runId) => new($"webdevloop/{runId}/integration");

    public static BranchName StackBranch(RunId runId, TicketRunId ticketId) => new($"stack/{runId}/{ticketId}");

    public static BranchName TicketBranch(RunId runId, TicketRunId ticketId) => new($"webdevloop/{runId}/ticket/{ticketId}");
}
