using WebDevLoop.Core.Domain;

namespace WebDevLoop.Core.Orchestration.Attention;

/// <summary>
/// A deterministic, cheap and safe fix for one reason code, run before anything else is asked of an agent or the user.
/// </summary>
public interface IKnownRemediation
{
    AttentionCode Code { get; }

    /// <summary>How often it may run for the same item before the user's next Retry; keeps a failing fix from looping.</summary>
    int MaxAttempts { get; }

    /// <param name="previousAttempts">How often it already ran for this item since the user last pressed Retry.</param>
    /// <returns>Resolved when the situation is repaired and verified; unresolved with what was tried otherwise.</returns>
    Task<AttentionStageResult> TryAsync(AttentionCase attentionCase, int previousAttempts, CancellationToken cancellationToken);
}
