using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Ports;

namespace WebDevLoop.Core.Orchestration.Recovery.ExternalState;

/// <summary>
/// Recreates the worktree of a reviewed ticket when it vanished from disk (e.g. the workspace was cleaned or restored), so
/// review and fix turns that run in it can resume. The worktree is rebuilt from the ticket branch, which keeps any commit
/// beyond the reviewed one. Worktrees that exist (clean, dirty, or locked) and worktrees of tickets with an active step are
/// left alone; implementing tickets are recovered with their agent step.
/// </summary>
public sealed class WorktreeReconciler(ITicketRunRepository ticketRuns, IStepRunRepository stepRuns, IGitWorkspace git) : ISpecReconciliationStep
{
    public async Task<IReadOnlyList<ReconciliationAction>> ReconcileAsync(SpecReconciliationContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        GitRepositoryLocation location = context.Location;
        var actions = new List<ReconciliationAction>();
        foreach (TicketRun ticket in await ticketRuns.ListBySpecRunAsync(context.Spec.Id, cancellationToken))
        {
            if (ticket is not { Status: TicketRunStatus.Reviewing or TicketRunStatus.FixingReviewFindings, WorktreePath: { } path, LastImplementedSha: { } reviewed }
                || (await stepRuns.ListByTicketRunAsync(ticket.Id, cancellationToken)).Any(step => step.IsActive)
                || (await git.InspectWorktreeAsync(location, path, cancellationToken)).Status != WorktreeStatus.Missing)
            {
                continue;
            }

            CommitSha head = await git.GetBranchTipAsync(location, ticket.BranchName, GitRefScope.Local, cancellationToken) ?? reviewed;
            await git.PrepareWorktreeAsync(location, new WorktreeSpec(ticket.BranchName, head, path), cancellationToken);
            actions.Add(new ReconciliationAction(context.Spec.Id, ticket.Id, ReconciliationActionKind.WorktreeRestored, $"'{path}' on '{ticket.BranchName}' at {head}"));
        }

        return actions;
    }
}
