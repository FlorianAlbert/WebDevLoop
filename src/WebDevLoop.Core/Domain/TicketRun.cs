namespace WebDevLoop.Core.Domain;

public sealed class TicketRun : VersionedEntity
{
    private TicketRun()
    {
        Title = string.Empty;
        BodySnapshot = string.Empty;
    }

    public TicketRunId Id { get; private set; }

    public RunId SpecRunId { get; private set; }

    public IssueRef Issue { get; private set; }

    public string Title { get; private set; }

    public string BodySnapshot { get; private set; }

    public TicketRunStatus Status { get; private set; }

    public int Attempt { get; private set; }

    public int ReviewIteration { get; private set; }

    public BranchName BranchName { get; private set; }

    public string? WorktreePath { get; set; }

    public CommitSha? LastImplementedSha { get; set; }

    public CommitSha? IntegratedCommitSha { get; set; }

    public PullRequestNumber? PullRequestNumber { get; set; }

    public int? StackPosition { get; set; }

    public string? FailureReason { get; private set; }

    /// <summary>The phase the ticket was in when it last moved to <see cref="TicketRunStatus.NeedsAttention"/>, so a retry can resume it.</summary>
    public TicketRunStatus? NeedsAttentionFrom { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public bool IsTerminal => Status.IsTerminal();

    public static TicketRun Create(
        TicketRunId id,
        RunId specRunId,
        IssueRef issue,
        string title,
        string bodySnapshot,
        DateTimeOffset createdAt) => new()
        {
            Id = id,
            BranchName = RunScopedNaming.TicketBranch(specRunId, id),
            SpecRunId = specRunId,
            Issue = issue,
            Title = title,
            BodySnapshot = bodySnapshot,
            CreatedAt = createdAt,
            UpdatedAt = createdAt,
        };

    public void TransitionTo(TicketRunStatus next, DateTimeOffset at)
    {
        if (!Status.CanTransitionTo(next))
        {
            throw new InvalidStatusTransitionException(nameof(TicketRun), Status, next);
        }

        Status = next;
        FailureReason = null;
        NeedsAttentionFrom = null;
        UpdatedAt = at;

        switch (next)
        {
            case TicketRunStatus.Implementing:
                Attempt++;
                ReviewIteration = 0;
                break;
            case TicketRunStatus.FixingReviewFindings:
                ReviewIteration++;
                break;
        }
    }

    /// <summary>
    /// The user's Retry of a ticket in <see cref="TicketRunStatus.NeedsAttention"/>: it resumes the phase that failed. A
    /// failed review (including exhausted review iterations) starts a fresh review round with a full iteration budget on
    /// the implemented commit; a failed integration resumes its saga; anything else, or a ticket without an implemented
    /// commit, is implemented again.
    /// </summary>
    /// <param name="integrationInProgress">
    /// The ticket's integration saga already squashed (its commit may be on the integration branch): whatever phase failed,
    /// the saga is resumed rather than implementing the ticket again.
    /// </param>
    /// <returns>The status the ticket moved to.</returns>
    public TicketRunStatus Retry(DateTimeOffset at, bool integrationInProgress = false)
    {
        if (Status != TicketRunStatus.NeedsAttention)
        {
            throw new InvalidStatusTransitionException(nameof(TicketRun), Status, TicketRunStatus.Ready);
        }

        TicketRunStatus target = (NeedsAttentionFrom, LastImplementedSha) switch
        {
            _ when integrationInProgress => TicketRunStatus.Integrating,
            (TicketRunStatus.Reviewing or TicketRunStatus.FixingReviewFindings, not null) => TicketRunStatus.Reviewing,
            (TicketRunStatus.Integrating, not null) => TicketRunStatus.Integrating,
            _ => TicketRunStatus.Ready,
        };
        TransitionTo(target, at);
        if (target == TicketRunStatus.Reviewing)
        {
            // A new round identity (attempt, iteration) so earlier round results are not read back as the current round.
            Attempt++;
            ReviewIteration = 0;
        }

        return target;
    }

    public void MarkNeedsAttention(string reason, DateTimeOffset at)
    {
        TicketRunStatus failedIn = Status;
        TransitionTo(TicketRunStatus.NeedsAttention, at);
        FailureReason = reason;
        NeedsAttentionFrom = failedIn;
    }
}
