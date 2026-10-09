namespace WebDevLoop.Core.Domain;

public sealed class IntegrationSaga : VersionedEntity
{
    private IntegrationSaga()
    {
        ExternalIdempotencyKey = string.Empty;
    }

    public long Id { get; private set; }

    public RunId SpecRunId { get; private set; }

    public TicketRunId TicketRunId { get; private set; }

    public CommitSha? ExpectedPriorIntegrationSha { get; private set; }

    public CommitSha? SquashCommitSha { get; set; }

    public BranchName StackBranchName { get; private set; }

    public PullRequestNumber? PullRequestNumber { get; set; }

    public int? StackNumber { get; set; }

    public IntegrationSagaCheckpoint Checkpoint { get; private set; }

    public string ExternalIdempotencyKey { get; private set; }

    public string? LastError { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public bool IsCompleted => Checkpoint == IntegrationSagaCheckpoint.Completed;

    public static IntegrationSaga Start(RunId specRunId, TicketRunId ticketRunId, CommitSha? expectedPriorIntegrationSha, DateTimeOffset at) => new()
    {
        SpecRunId = specRunId,
        TicketRunId = ticketRunId,
        StackBranchName = RunScopedNaming.StackBranch(specRunId, ticketRunId),
        ExternalIdempotencyKey = $"{specRunId}:{ticketRunId}",
        ExpectedPriorIntegrationSha = expectedPriorIntegrationSha,
        CreatedAt = at,
        UpdatedAt = at,
    };

    /// <summary>Moves forward only; re-advancing to the current checkpoint is a no-op so recovery can replay safely.</summary>
    public void AdvanceTo(IntegrationSagaCheckpoint checkpoint, DateTimeOffset at)
    {
        if (checkpoint < Checkpoint)
        {
            throw new InvalidStatusTransitionException(nameof(IntegrationSaga), Checkpoint, checkpoint);
        }

        if (checkpoint == Checkpoint)
        {
            return;
        }

        Checkpoint = checkpoint;
        LastError = null;
        UpdatedAt = at;
    }

    public void RecordError(string error, DateTimeOffset at)
    {
        LastError = error;
        UpdatedAt = at;
    }
}
