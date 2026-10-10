using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Orchestration.TicketExecution;
using WebDevLoop.Core.Ports;

namespace WebDevLoop.Core.Orchestration.Attention;

/// <summary>
/// A ticket worktree is WebDevLoop's own: tracked changes are saved as a patch in the run folder, the worktree is reset to its
/// commit and untracked and ignored files inside it are removed (see <see cref="WorktreeRemediator"/>), then the checkout is
/// verified again. The reviewed commit stays the source of truth; nothing is committed on anyone's behalf.
/// </summary>
public sealed class WorktreeNotCleanRemediation(AttentionWorkLoader loader, WorktreeRemediator remediator, IGitWorkspace git) : IKnownRemediation
{
    public AttentionCode Code => AttentionCode.WorktreeNotClean;

    public int MaxAttempts => 2;

    public async Task<AttentionStageResult> TryAsync(AttentionCase attentionCase, int previousAttempts, CancellationToken cancellationToken)
    {
        if (await loader.LoadAsync(attentionCase, cancellationToken) is not { Ticket: { } ticket } work)
        {
            return AttentionStageResult.Unresolved("The ticket or its repository no longer exists.");
        }

        string path = work.TicketWorktreePath!;
        WorktreeRemediation cleaned = await remediator.RemediateAsync(work.Spec.Id, ticket.Id, work.Location, work.Layout, path, cancellationToken);
        WorktreeInspection inspection = await git.InspectWorktreeAsync(work.Location, path, cancellationToken);
        bool usable = inspection is { Status: WorktreeStatus.Clean, Head: { } head }
            && inspection.Branch == ticket.BranchName
            && (ticket.LastImplementedSha is not { } reviewed || await git.IsAncestorAsync(work.Location, reviewed, head, cancellationToken));
        if (!usable)
        {
            return AttentionStageResult.Unresolved(
                $"Cleaning the working folder did not give a clean checkout (it is {inspection.Status}).",
                $"Cleaned the working folder ({Describe(cleaned)}); it is still {inspection.Status}.");
        }

        return AttentionStageResult.Resolved($"The working folder {Describe(cleaned)} and was verified clean; continuing.", AttentionResume.Retry);
    }

    private static string Describe(WorktreeRemediation cleaned) => cleaned.Performed
        ? $"had {cleaned.TrackedFiles.Count} changed and {cleaned.RemovedPaths.Count} untracked or ignored file(s), which were removed"
            + (cleaned.PatchPath is null ? string.Empty : $" (changes saved to {cleaned.PatchPath})")
        : "needed no cleaning";
}
