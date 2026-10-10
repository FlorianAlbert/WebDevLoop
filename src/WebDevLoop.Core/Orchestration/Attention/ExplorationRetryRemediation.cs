using WebDevLoop.Core.Domain;

namespace WebDevLoop.Core.Orchestration.Attention;

/// <summary>An exploration that failed once is run again with the same input; a second failure goes to the user.</summary>
public sealed class ExplorationRetryRemediation : IKnownRemediation
{
    public AttentionCode Code => AttentionCode.ExplorationFailed;

    public int MaxAttempts => 1;

    public Task<AttentionStageResult> TryAsync(AttentionCase attentionCase, int previousAttempts, CancellationToken cancellationToken) =>
        Task.FromResult(AttentionStageResult.Resolved("The exploration failed; started the preparation, including a new exploration, once more.", AttentionResume.Retry));
}
