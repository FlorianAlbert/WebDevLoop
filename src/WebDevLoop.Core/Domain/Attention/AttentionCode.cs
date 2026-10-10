namespace WebDevLoop.Core.Domain;

/// <summary>
/// Why a run or ticket needs attention: one code per situation of the catalogue. The code is persisted by name, so a member
/// is never renamed or reused.
/// </summary>
public enum AttentionCode
{
    // Preparation
    RepositoryNotRegistered,
    SpecHasNoTickets,
    TicketDependencyCycle,
    BaseBranchMissing,
    IntegrationBranchExists,
    ExplorationFailed,

    // Implementation
    ImplementationFailed,
    ImplementerBlocked,
    PromptNotRenderable,
    WorktreeNotClean,
    TicketBranchNotBasedOnIntegration,
    ReportedCommitMismatch,

    // Review
    ReviewFailed,
    ReviewIterationsExhausted,
    FixFailed,

    // Integration
    IntegrationTemporaryFailure,
    IntegrationFailed,
    TicketHasNoChanges,
    MergeConflictUnresolved,
    IntegrationBranchMoved,
    IntegrationPushRejected,
    StackBranchExists,
    PullRequestNotOpen,
    PullRequestStackChanged,
    DiffVerificationFailed,
    ForeignPullRequest,

    // Recovery
    InterruptedRepeatedly,

    // Parent review, testing, completion and merge tracking
    ParentReviewFailed,
    ParentReviewCycleLimit,
    NoNewWork,
    TesterBlocked,
    TestingFailed,
    TestCycleLimit,
    IntegrationTipNotTested,
    StackVerificationFailed,
    PullRequestsClosedUnmerged,
    TrunkMissingStack,

    // Any
    InternalInconsistency,

    /// <summary>Stored before reasons were structured, or created by an older version; only used to render such rows.</summary>
    Unclassified,
}
