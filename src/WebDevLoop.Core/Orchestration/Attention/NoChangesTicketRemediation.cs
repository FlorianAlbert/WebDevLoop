using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Ports;

namespace WebDevLoop.Core.Orchestration.Attention;

/// <summary>
/// A reviewed ticket branch that adds nothing to the integration branch has no change to publish. Both reviewers approved
/// it, so its goal is considered already met: the ticket is skipped as "no changes needed" (dependents are released) and the
/// run history says so. Only done when nothing of the ticket was integrated or published before.
/// </summary>
public sealed class NoChangesTicketRemediation(AttentionWorkLoader loader, IPullStackLayerRepository layers) : IKnownRemediation
{
    public AttentionCode Code => AttentionCode.TicketHasNoChanges;

    public int MaxAttempts => 1;

    public async Task<AttentionStageResult> TryAsync(AttentionCase attentionCase, int previousAttempts, CancellationToken cancellationToken)
    {
        if (await loader.LoadAsync(attentionCase, cancellationToken) is not { Ticket: { } ticket } work)
        {
            return AttentionStageResult.Unresolved("The ticket no longer exists.");
        }

        bool published = ticket.IntegratedCommitSha is not null
            || (await layers.ListBySpecRunAsync(work.Spec.Id, cancellationToken)).Any(layer => layer.TicketRunId == ticket.Id);
        return published
            ? AttentionStageResult.Unresolved("Part of the ticket was published already, so it is not skipped automatically.")
            : AttentionStageResult.Resolved(
                $"Ticket #{ticket.Issue.Number} needs no changes: the reviewed branch adds nothing to the integration branch, so it was skipped.", AttentionResume.Skip);
    }
}
