using WebDevLoop.Core.Domain;

namespace WebDevLoop.Core.Orchestration.Attention;

/// <summary>
/// GitHub or the network failed during integration: wait with a growing pause, then resume the saga from its last checkpoint
/// (every saga step is replay-safe). The pauses bound the attempts.
/// </summary>
public sealed class TransientFailureRemediation(AttentionTriageOptions options) : IKnownRemediation
{
    public AttentionCode Code => AttentionCode.IntegrationTemporaryFailure;

    public int MaxAttempts => options.TransientBackoff.Count;

    public async Task<AttentionStageResult> TryAsync(AttentionCase attentionCase, int previousAttempts, CancellationToken cancellationToken)
    {
        TimeSpan pause = options.TransientBackoff[Math.Min(previousAttempts, options.TransientBackoff.Count - 1)];
        await options.Delay(pause, cancellationToken);
        return AttentionStageResult.Resolved(
            $"GitHub or the network had failed; waited {Format(pause)} and resumed publishing (attempt {previousAttempts + 1} of {options.TransientBackoff.Count}).",
            AttentionResume.Retry);
    }

    private static string Format(TimeSpan pause) => pause.TotalMinutes >= 1 ? $"{pause.TotalMinutes:0.#} min" : $"{pause.TotalSeconds:0} s";
}
