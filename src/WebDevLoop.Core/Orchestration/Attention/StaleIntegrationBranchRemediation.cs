using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Ports;

namespace WebDevLoop.Core.Orchestration.Attention;

/// <summary>
/// The integration branch name embeds the run id, so a branch that already exists is the leftover of an earlier attempt of
/// the same run. As long as no ticket was integrated into it yet, nothing on it is worth keeping: it is reset to the run's
/// starting point. A branch that carries layers is left alone.
/// </summary>
public sealed class StaleIntegrationBranchRemediation(AttentionWorkLoader loader, ITicketRunRepository ticketRuns, IPullStackLayerRepository layers, IGitWorkspace git) : IKnownRemediation
{
    public AttentionCode Code => AttentionCode.IntegrationBranchExists;

    public int MaxAttempts => 1;

    public async Task<AttentionStageResult> TryAsync(AttentionCase attentionCase, int previousAttempts, CancellationToken cancellationToken)
    {
        if (await loader.LoadAsync(attentionCase, cancellationToken) is not { } work || work.Spec.IntegrationBaseSha is not { } baseSha)
        {
            return AttentionStageResult.Unresolved("The run or its starting point is missing.");
        }

        IReadOnlyList<TicketRun> tickets = await ticketRuns.ListBySpecRunAsync(work.Spec.Id, cancellationToken);
        bool nothingIntegrated = work.Spec.IntegrationTipSha == baseSha
            && tickets.All(ticket => ticket.IntegratedCommitSha is null)
            && (await layers.ListBySpecRunAsync(work.Spec.Id, cancellationToken)).Count == 0;
        if (!nothingIntegrated)
        {
            return AttentionStageResult.Unresolved("The branch already carries integrated tickets, so it is not reset.");
        }

        CommitSha? actual = await git.GetBranchTipAsync(work.Location, work.Spec.IntegrationBranch, GitRefScope.Local, cancellationToken);
        if (actual is null)
        {
            return AttentionStageResult.Resolved("The leftover integration branch is gone; preparing the run again.", AttentionResume.Retry);
        }

        RefUpdateResult reset = await git.UpdateBranchAsync(work.Location, work.Spec.IntegrationBranch, baseSha, actual, cancellationToken);
        return reset.Succeeded
            ? AttentionStageResult.Resolved($"Reset the leftover integration branch from {actual} to the run's starting point {baseSha} and prepared the run again.", AttentionResume.Retry)
            : AttentionStageResult.Unresolved("The leftover branch changed while it was being reset.");
    }
}
