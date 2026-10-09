using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Ports;

namespace WebDevLoop.Core.Orchestration.Integration;

/// <summary>
/// Title, body, and commit message of a ticket's stack layer. The PR references its ticket and parent spec without a
/// closing keyword: upper layers target a stack branch, where GitHub ignores closing keywords, so the saga transitions the
/// ticket explicitly. The run and ticket ids travel in the port contract, from which the adapter appends its marker.
/// </summary>
internal static class StackLayerPullRequest
{
    public static DraftPullRequest Draft(IntegrationContext context, BranchName baseBranch) =>
        new(context.Saga.StackBranchName, baseBranch, context.Ticket.Title, Body(context), context.Spec.Id, context.Ticket.Id);

    /// <summary>Commit metadata that lets recovery recognise the squash commit of a run's ticket.</summary>
    public static string SquashMessage(IntegrationContext context)
    {
        (SpecRun spec, TicketRun ticket) = (context.Spec, context.Ticket);
        return $"{ticket.Title} ({IssueReferences.Format(ticket.Issue, context.Repository.Ref)})\n\n"
            + $"WebDevLoop-Run: {spec.Id}\nWebDevLoop-Ticket: {ticket.Id}";
    }

    private static string Body(IntegrationContext context)
    {
        (SpecRun spec, TicketRun ticket) = (context.Spec, context.Ticket);
        GitHubRepoRef repository = context.Repository.Ref;
        return $"Stack layer for ticket {IssueReferences.Format(ticket.Issue, repository)}: {ticket.Title}\n\n"
            + $"Part of parent spec {IssueReferences.Format(spec.ParentIssue, repository)}: {spec.Title}. "
            + "This pull request contains exactly one squash commit with the ticket's changes. WebDevLoop transitions the ticket "
            + "itself, because pull requests above the bottom of a stack do not auto-close issues.\n\n"
            + $"WebDevLoop run `{spec.Id}`, ticket run `{ticket.Id}`.";
    }
}
