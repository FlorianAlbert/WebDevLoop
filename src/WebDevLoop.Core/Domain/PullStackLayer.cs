namespace WebDevLoop.Core.Domain;

public sealed class PullStackLayer
{
    private PullStackLayer()
    {
    }

    public long Id { get; private set; }

    public RunId SpecRunId { get; private set; }

    public TicketRunId TicketRunId { get; private set; }

    public BranchName BranchName { get; private set; }

    public CommitSha CommitSha { get; private set; }

    public PullRequestNumber PullRequestNumber { get; private set; }

    public BranchName BaseBranch { get; private set; }

    public int? StackNumber { get; set; }

    public int Position { get; private set; }

    public bool IsDraft { get; private set; }

    public CommitSha? VerifiedDiffSha { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public static PullStackLayer Create(
        RunId specRunId,
        TicketRunId ticketRunId,
        BranchName branchName,
        CommitSha commitSha,
        PullRequestNumber pullRequestNumber,
        BranchName baseBranch,
        int position,
        DateTimeOffset at)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(position, 1);

        return new()
        {
            SpecRunId = specRunId,
            TicketRunId = ticketRunId,
            BranchName = branchName,
            CommitSha = commitSha,
            PullRequestNumber = pullRequestNumber,
            BaseBranch = baseBranch,
            Position = position,
            IsDraft = true,
            CreatedAt = at,
            UpdatedAt = at,
        };
    }

    public void MarkReady(DateTimeOffset at)
    {
        IsDraft = false;
        UpdatedAt = at;
    }

    public void RecordVerifiedDiff(CommitSha diffSha, DateTimeOffset at)
    {
        VerifiedDiffSha = diffSha;
        UpdatedAt = at;
    }
}
