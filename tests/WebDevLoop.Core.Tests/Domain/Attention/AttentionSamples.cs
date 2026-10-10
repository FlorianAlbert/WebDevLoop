using WebDevLoop.Core.Domain;

namespace WebDevLoop.Core.Tests.Domain.Attention;

/// <summary>
/// One sample reason per <see cref="AttentionCode"/>. The switch has no default arm on purpose: a new code without a sample
/// here (and so without a factory in <see cref="AttentionReasons"/>) fails the tests that enumerate every code.
/// </summary>
internal static class AttentionSamples
{
    private const string Repo = "octo/app";

    public static AttentionReason For(AttentionCode code) => code switch
    {
        AttentionCode.RepositoryNotRegistered => AttentionReasons.RepositoryNotRegistered(7),
        AttentionCode.SpecHasNoTickets => AttentionReasons.SpecHasNoTickets("Spec octo/app#1 has no open ticket sub-issues."),
        AttentionCode.TicketDependencyCycle => AttentionReasons.TicketDependencyCycle("The ticket dependency graph contains a cycle: a -> b -> a"),
        AttentionCode.BaseBranchMissing => AttentionReasons.BaseBranchMissing("trunk", Repo, "Base branch 'trunk' does not exist on the remote of octo/app."),
        AttentionCode.IntegrationBranchExists => AttentionReasons.IntegrationBranchExists("webdevloop/r1/integration", "Integration branch already exists at abc, expected def.", "/clones/app"),
        AttentionCode.ExplorationFailed => AttentionReasons.ExplorationFailed("Exploration failed after 3 attempt(s): timed out"),
        AttentionCode.ImplementationFailed => AttentionReasons.ImplementationFailed(3, "timed out"),
        AttentionCode.ImplementerBlocked => AttentionReasons.ImplementerBlocked("I have no access to the database"),
        AttentionCode.PromptNotRenderable => AttentionReasons.PromptNotRenderable("Implementer", "The implementer prompt cannot be rendered: {bad_placeholder}"),
        AttentionCode.WorktreeNotClean => AttentionReasons.WorktreeNotClean("/work/runs/r1/tickets/t1", "webdevloop/r1/ticket/t1", "Worktree '/work/runs/r1/tickets/t1' is Dirty on 'b'."),
        AttentionCode.TicketBranchNotBasedOnIntegration => AttentionReasons.TicketBranchNotBasedOnIntegration("webdevloop/r1/ticket/t1", "Branch does not contain the integration tip."),
        AttentionCode.ReportedCommitMismatch => AttentionReasons.ReportedCommitMismatch("Implementer reported abc, but the branch is at def."),
        AttentionCode.ReviewFailed => AttentionReasons.ReviewFailed("The correctness review failed after 3 attempt(s): timed out"),
        AttentionCode.ReviewIterationsExhausted => AttentionReasons.ReviewIterationsExhausted(2, 5, 5),
        AttentionCode.FixFailed => AttentionReasons.FixFailed(3, "timed out"),
        AttentionCode.IntegrationTemporaryFailure => AttentionReasons.IntegrationTemporaryFailure(3, "StackBranchPushed", "GitHub request failed: timeout"),
        AttentionCode.IntegrationFailed => AttentionReasons.IntegrationFailed(3, "StackBranchPushed", "Resource not accessible by integration"),
        AttentionCode.TicketHasNoChanges => AttentionReasons.TicketHasNoChanges("webdevloop/r1/ticket/t1", "abc", "def"),
        AttentionCode.MergeConflictUnresolved => AttentionReasons.MergeConflictUnresolved("Squash-merging still conflicts in calc.py after 3 conflict resolution attempt(s).", "/work/runs/r1/tickets/t1"),
        AttentionCode.IntegrationBranchMoved => AttentionReasons.IntegrationBranchMoved("webdevloop/r1/integration", "Integration branch is at abc instead of the expected prior tip def."),
        AttentionCode.IntegrationPushRejected => AttentionReasons.IntegrationPushRejected("webdevloop/r1/integration", "Pushing was rejected: the remote branch is not at abc."),
        AttentionCode.StackBranchExists => AttentionReasons.StackBranchExists("stack/r1/t1", "Stack branch already exists on the remote at another commit."),
        AttentionCode.PullRequestNotOpen => AttentionReasons.PullRequestNotOpen(4, "Closed", "Pull request #4 for 'stack/r1/t1' is Closed.", Repo),
        AttentionCode.PullRequestStackChanged => AttentionReasons.PullRequestStackChanged("Pull request #5 is not directly above #4 in stack 2.", Repo),
        AttentionCode.DiffVerificationFailed => AttentionReasons.DiffVerificationFailed(4, "the layer commit changes files the ticket branch does not: x.py", Repo),
        AttentionCode.ForeignPullRequest => AttentionReasons.ForeignPullRequest(9, "stack/r1/t1", "Pull request #9 does not identify run r1.", Repo),
        AttentionCode.InterruptedRepeatedly => AttentionReasons.InterruptedRepeatedly("Implement", 3, 2, forTicket: true),
        AttentionCode.ParentReviewFailed => AttentionReasons.ParentReviewFailed("The parent-spec review completed without a persisted result."),
        AttentionCode.ParentReviewCycleLimit => AttentionReasons.ParentReviewCycleLimit(2, 3, 3, "Still 2 issue(s) after 3 cycle(s)."),
        AttentionCode.NoNewWork => AttentionReasons.NoNewWork("test", "Test cycle 2 only repeated issues whose tickets are already done (#5)."),
        AttentionCode.TesterBlocked => AttentionReasons.TesterBlocked("The app does not start"),
        AttentionCode.TestingFailed => AttentionReasons.TestingFailed("The tester failed after 3 attempt(s): timed out"),
        AttentionCode.TestCycleLimit => AttentionReasons.TestCycleLimit(3, "The tester still found 2 issue(s) after 3 test cycle(s)."),
        AttentionCode.IntegrationTipNotTested => AttentionReasons.IntegrationTipNotTested("The integration tip abc was not tested."),
        AttentionCode.StackVerificationFailed => AttentionReasons.StackVerificationFailed("PR #4 base is wrong", Repo),
        AttentionCode.PullRequestsClosedUnmerged => AttentionReasons.PullRequestsClosedUnmerged("#4, #5", "main", Repo),
        AttentionCode.TrunkMissingStack => AttentionReasons.TrunkMissingStack("Every PR is merged, but 'main' does not contain the top layer.", "main"),
        AttentionCode.InternalInconsistency => AttentionReasons.InternalInconsistency("Spec run or repository of ticket 't1' is missing.", forTicket: true),
        AttentionCode.Unclassified => AttentionReasons.Unclassified("old reason", forTicket: false),
        _ => throw new ArgumentOutOfRangeException(nameof(code), code, "Add a sample for the new reason code."),
    };
}
