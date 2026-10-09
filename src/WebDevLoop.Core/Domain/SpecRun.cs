namespace WebDevLoop.Core.Domain;

public sealed class SpecRun : VersionedEntity
{
    private SpecRun()
    {
        Title = string.Empty;
        BodySnapshot = string.Empty;
    }

    public RunId Id { get; private set; }

    public int RepositoryId { get; private set; }

    public IssueRef ParentIssue { get; private set; }

    public string Title { get; private set; }

    public string BodySnapshot { get; private set; }

    public SpecRunStatus Status { get; private set; }

    public int QueuePosition { get; set; }

    public BranchName? BaseBranch { get; set; }

    public BranchName IntegrationBranch { get; private set; }

    public CommitSha? IntegrationBaseSha { get; set; }

    public CommitSha? IntegrationTipSha { get; set; }

    public SpecDependencyMode? DependencyModeUsed { get; set; }

    public int? MaxActiveSpecsSlot { get; set; }

    public int ReviewCycle { get; private set; }

    public int TestCycle { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? StartedAt { get; private set; }

    public DateTimeOffset? ReadyAt { get; private set; }

    public DateTimeOffset? CompletedAt { get; private set; }

    public string? FailureReason { get; private set; }

    /// <summary>The phase the run was in when it last moved to <see cref="SpecRunStatus.NeedsAttention"/>, so a retry can resume it.</summary>
    public SpecRunStatus? NeedsAttentionFrom { get; private set; }

    public bool IsActive => Status.IsActive();

    public bool IsTerminal => Status.IsTerminal();

    public static SpecRun Queue(
        RunId id,
        int repositoryId,
        IssueRef parentIssue,
        string title,
        string bodySnapshot,
        int queuePosition,
        DateTimeOffset createdAt) => new()
        {
            Id = id,
            IntegrationBranch = RunScopedNaming.IntegrationBranch(id),
            RepositoryId = repositoryId,
            ParentIssue = parentIssue,
            Title = title,
            BodySnapshot = bodySnapshot,
            QueuePosition = queuePosition,
            CreatedAt = createdAt,
        };

    public void TransitionTo(SpecRunStatus next, DateTimeOffset at)
    {
        if (!Status.CanTransitionTo(next))
        {
            throw new InvalidStatusTransitionException(nameof(SpecRun), Status, next);
        }

        Status = next;
        FailureReason = null;
        NeedsAttentionFrom = null;
        if (!next.IsActive())
        {
            MaxActiveSpecsSlot = null;
        }

        switch (next)
        {
            case SpecRunStatus.Preparing:
                StartedAt ??= at;
                break;
            case SpecRunStatus.ParentReviewing:
                ReviewCycle++;
                break;
            case SpecRunStatus.Testing:
                TestCycle++;
                break;
            case SpecRunStatus.ReadyForReview:
                ReadyAt = at;
                break;
        }

        if (next.IsTerminal())
        {
            CompletedAt = at;
        }
    }

    public void MarkNeedsAttention(string reason, DateTimeOffset at)
    {
        SpecRunStatus failedIn = Status;
        TransitionTo(SpecRunStatus.NeedsAttention, at);
        FailureReason = reason;
        NeedsAttentionFrom = failedIn;
    }
}
