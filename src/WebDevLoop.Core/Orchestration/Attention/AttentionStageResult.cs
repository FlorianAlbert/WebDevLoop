using WebDevLoop.Core.Domain;

namespace WebDevLoop.Core.Orchestration.Attention;

public enum AttentionStageStatus
{
    /// <summary>The stage has nothing to offer for this reason; the next stage looks at it.</summary>
    NotApplicable,

    /// <summary>The stage repaired the situation and verified it; the owner is resumed.</summary>
    Resolved,

    /// <summary>The stage tried and failed (or ran out of attempts); what it did is recorded and the next stage looks at it.</summary>
    Unresolved,
}

public enum AttentionResumeKind
{
    /// <summary>Resume the phase that failed, as the user's Retry would.</summary>
    Retry,

    /// <summary>Give the ticket up without its change, as the user's Skip would.</summary>
    SkipTicket,
}

/// <param name="RetryAt">For <see cref="AttentionResumeKind.Retry"/> of a ticket: resume here instead of at the failed phase.</param>
public sealed record AttentionResume(AttentionResumeKind Kind, TicketRunStatus? RetryAt = null)
{
    public static AttentionResume Retry { get; } = new(AttentionResumeKind.Retry);

    public static AttentionResume RetryAtReview { get; } = new(AttentionResumeKind.Retry, TicketRunStatus.Reviewing);

    public static AttentionResume Skip { get; } = new(AttentionResumeKind.SkipTicket);
}

/// <param name="Summary">What happened, in the words that appear in the run history ("Removed 3 untracked files and continued").</param>
/// <param name="Tried">What was attempted, for the "What WebDevLoop already tried" list when the user is asked after all.</param>
public sealed record AttentionStageResult(AttentionStageStatus Status, string Summary, IReadOnlyList<string> Tried, AttentionResume? Resume = null)
{
    public static AttentionStageResult NotApplicable { get; } = new(AttentionStageStatus.NotApplicable, string.Empty, []);

    public static AttentionStageResult Resolved(string summary, AttentionResume resume) =>
        new(AttentionStageStatus.Resolved, summary, [summary], resume);

    public static AttentionStageResult Unresolved(string summary, params string[] tried) =>
        new(AttentionStageStatus.Unresolved, summary, tried.Length == 0 ? [summary] : tried);
}
