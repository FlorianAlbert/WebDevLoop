namespace WebDevLoop.Core.Domain;

/// <summary>Ordered checkpoints; a saga only moves forward.</summary>
public enum IntegrationSagaCheckpoint
{
    Started,
    SquashCommitCreated,
    IntegrationRefUpdated,
    IntegrationPushed,
    StackBranchPushed,
    PrCreated,
    StackLinked,
    DiffVerified,
    IssueTransitioned,
    Completed,
}
