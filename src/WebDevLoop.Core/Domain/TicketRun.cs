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

    public void MarkNeedsAttention(string reason, DateTimeOffset at)
    {
        TransitionTo(TicketRunStatus.NeedsAttention, at);
        FailureReason = reason;
    }
}
