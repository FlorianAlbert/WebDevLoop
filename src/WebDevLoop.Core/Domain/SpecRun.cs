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


    public int? MaxActiveSpecsSlot { get; set; }

    public int ReviewCycle { get; private set; }

    public int TestCycle { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? StartedAt { get; private set; }

    /// <summary>When the run last moved to its current status (its creation while still queued).</summary>
    public DateTimeOffset StatusChangedAt { get; private set; }

    public DateTimeOffset? ReadyAt { get; private set; }

    public DateTimeOffset? CompletedAt { get; private set; }

    /// <summary>The current recoverable work-decision reason, or the reason this run finished without further work.</summary>
    public string? WorkDecisionReason { get; private set; }

    /// <summary>The technical wording of <see cref="Attention"/>; null unless the run needs attention.</summary>
    public string? FailureReason { get; private set; }

    /// <summary>Structured guidance for the user while the run is in <see cref="SpecRunStatus.NeedsAttention"/>.</summary>
    public AttentionReason? Attention { get; private set; }

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
            StatusChangedAt = createdAt,
        };

    public void TransitionTo(SpecRunStatus next, DateTimeOffset at)
    {
        if (!Status.CanTransitionTo(next))
        {
            throw new InvalidStatusTransitionException(nameof(SpecRun), Status, next);
        }

        AdvanceCycle(next);
        Status = next;
        StatusChangedAt = at;
        WorkDecisionReason = null;
        FailureReason = null;
        Attention = null;
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
            case SpecRunStatus.ReadyForReview:
                ReadyAt = at;
                break;
        }

        if (next.IsTerminal())
        {
            CompletedAt = at;
        }
    }

    private void AdvanceCycle(SpecRunStatus next)
    {
        if (Status == SpecRunStatus.NeedsAttention
            && Attention?.Code == AttentionCode.ModelUnavailable
            && NeedsAttentionFrom == next)
        {
            return;
        }

        switch (next)
        {
            case SpecRunStatus.ParentReviewing:
                ReviewCycle++;
                break;
            case SpecRunStatus.Testing:
                TestCycle++;
                break;
        }
    }

    public void MarkNeedsAttention(AttentionReason reason, DateTimeOffset at)
    {
        ArgumentNullException.ThrowIfNull(reason);
        SpecRunStatus failedIn = Status;
        TransitionTo(SpecRunStatus.NeedsAttention, at);
        FailureReason = reason.Details;
        Attention = reason;
        NeedsAttentionFrom = failedIn;
    }

    /// <summary>Records a current work-selection deferral or an explicit stopped/no-work completion outcome.</summary>
    public void SetWorkDecisionReason(string? reason)
    {
        if (Status is not (SpecRunStatus.Queued or SpecRunStatus.WaitingForDependency
            or SpecRunStatus.Running or SpecRunStatus.WaitingForTickets or SpecRunStatus.Completed))
        {
            throw new InvalidOperationException($"Spec run '{Id}' is {Status}; only queued, waiting, running or completed work decisions have a reason.");
        }

        WorkDecisionReason = reason;
    }

    /// <summary>Replaces the guidance of a parked run, e.g. with what the remediation pipeline tried.</summary>
    public void UpdateAttention(AttentionReason reason)
    {
        ArgumentNullException.ThrowIfNull(reason);
        if (Status != SpecRunStatus.NeedsAttention)
        {
            throw new InvalidOperationException($"Spec run '{Id}' is {Status}; only a spec run that needs attention has guidance to update.");
        }

        FailureReason = reason.Details;
        Attention = reason;
    }
}
