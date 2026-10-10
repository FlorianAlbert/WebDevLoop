using WebDevLoop.Core.Domain;

namespace WebDevLoop.Core.Orchestration.Attention;

/// <summary>The check WebDevLoop runs itself before it believes a troubleshooter's claim that the problem is solved.</summary>
public enum TroubleshooterCheck
{
    /// <summary>The ticket worktree is a clean checkout of the ticket branch and every commit the ticket had is still there.</summary>
    CleanTicketWorktree,

    /// <summary>The above, and the branch contains the integration tip, so the next phase's own check passes.</summary>
    TicketBasedOnIntegration,
}

/// <summary>Whether a reason code is worth an agent session, and how a claim that it was solved is verified.</summary>
/// <param name="Check">Null exactly when <paramref name="Escalates"/> is false.</param>
/// <param name="Rationale">Why the code does or does not escalate; the audit trail of the decision.</param>
public sealed record EscalationRule(bool Escalates, TroubleshooterCheck? Check, string Rationale)
{
    public static EscalationRule To(TroubleshooterCheck check, string rationale) => new(true, check, rationale);

    public static EscalationRule Not(string rationale) => new(false, null, rationale);
}

/// <summary>
/// Which reason codes escalate to the troubleshooter agent before the user is asked. Judgement is needed and a repair can be
/// verified for problems inside the ticket's own worktree: unexpected git states, an agent that failed or reported blocked,
/// an integration that failed with an unfamiliar error, a conflict the conflict resolver could not handle.
/// Everything else never reaches an agent: deterministic problems have a fixed remediation or a fixed instruction, decisions
/// and credentials belong to the user, GitHub and remote state is never changed by an agent, and a problem of the whole run
/// has no ticket worktree to repair and no check to verify a repair with.
/// </summary>
public static class TroubleshooterEscalation
{
    private const string UserDecision = "A decision for the user; an agent cannot make it for them.";
    private const string UserFixes = "Something outside WebDevLoop (settings, spec, GitHub) that only the user can change.";
    private const string GitHubState = "Remote and GitHub state; an agent must never change it, and the user gets exact steps instead.";
    private const string Deterministic = "Deterministic: the catalogue already says exactly what to do.";
    private const string RunLevel = "A problem of the whole run: there is no ticket worktree to repair and no check to verify a repair with.";

    private static readonly IReadOnlyDictionary<AttentionCode, EscalationRule> Rules = new Dictionary<AttentionCode, EscalationRule>
    {
        [AttentionCode.RepositoryNotRegistered] = EscalationRule.Not(UserFixes),
        [AttentionCode.SpecHasNoTickets] = EscalationRule.Not(UserFixes),
        [AttentionCode.TicketDependencyCycle] = EscalationRule.Not(UserFixes),
        [AttentionCode.BaseBranchMissing] = EscalationRule.Not(UserFixes),
        [AttentionCode.IntegrationBranchExists] = EscalationRule.Not(GitHubState),
        [AttentionCode.ExplorationFailed] = EscalationRule.Not(RunLevel),

        [AttentionCode.ImplementationFailed] = EscalationRule.To(TroubleshooterCheck.CleanTicketWorktree, "The implementer failed repeatedly; an agent can find out whether the environment or the worktree is at fault."),
        [AttentionCode.ImplementerBlocked] = EscalationRule.To(TroubleshooterCheck.CleanTicketWorktree, "The blocker may be an environment problem an agent can fix."),
        [AttentionCode.PromptNotRenderable] = EscalationRule.Not(Deterministic),
        [AttentionCode.WorktreeNotClean] = EscalationRule.To(TroubleshooterCheck.CleanTicketWorktree, "The deterministic cleaning did not give a clean checkout: an unexpected git state."),
        [AttentionCode.TicketBranchNotBasedOnIntegration] = EscalationRule.To(TroubleshooterCheck.TicketBasedOnIntegration, "The automatic merge failed, so the branch needs judgement."),
        [AttentionCode.ReportedCommitMismatch] = EscalationRule.To(TroubleshooterCheck.CleanTicketWorktree, "The agent's report and the repository disagree: an unexpected git state."),

        [AttentionCode.ReviewFailed] = EscalationRule.To(TroubleshooterCheck.CleanTicketWorktree, "A reviewer failed to produce a review; an agent can check the checkout and tools."),
        [AttentionCode.ReviewIterationsExhausted] = EscalationRule.Not(UserDecision),
        [AttentionCode.FixFailed] = EscalationRule.To(TroubleshooterCheck.CleanTicketWorktree, "The fixer failed repeatedly; an agent can find out whether the environment or the worktree is at fault."),

        [AttentionCode.IntegrationTemporaryFailure] = EscalationRule.Not("A temporary GitHub or network failure that the bounded retries already cover; an agent cannot repair an outage."),
        [AttentionCode.IntegrationFailed] = EscalationRule.To(TroubleshooterCheck.CleanTicketWorktree, "An integration step failed with an unfamiliar error; the ticket worktree is the part an agent may inspect and repair."),
        [AttentionCode.TicketHasNoChanges] = EscalationRule.Not(UserDecision),
        [AttentionCode.MergeConflictUnresolved] = EscalationRule.To(TroubleshooterCheck.TicketBasedOnIntegration, "The conflict resolver could not resolve the conflict; the troubleshooter inspects the state next."),
        [AttentionCode.IntegrationBranchMoved] = EscalationRule.Not(GitHubState),
        [AttentionCode.IntegrationPushRejected] = EscalationRule.Not(GitHubState),
        [AttentionCode.StackBranchExists] = EscalationRule.Not(GitHubState),
        [AttentionCode.PullRequestNotOpen] = EscalationRule.Not(GitHubState),
        [AttentionCode.PullRequestStackChanged] = EscalationRule.Not(GitHubState),
        [AttentionCode.DiffVerificationFailed] = EscalationRule.Not(GitHubState),
        [AttentionCode.ForeignPullRequest] = EscalationRule.Not(GitHubState),

        [AttentionCode.InterruptedRepeatedly] = EscalationRule.To(TroubleshooterCheck.CleanTicketWorktree, "The work was interrupted over and over; an agent can look for what keeps killing it in the worktree."),

        [AttentionCode.ParentReviewFailed] = EscalationRule.Not(RunLevel),
        [AttentionCode.ParentReviewCycleLimit] = EscalationRule.Not(UserDecision),
        [AttentionCode.NoNewWork] = EscalationRule.Not(UserDecision),
        [AttentionCode.TesterBlocked] = EscalationRule.Not(RunLevel),
        [AttentionCode.TestingFailed] = EscalationRule.Not(RunLevel),
        [AttentionCode.TestCycleLimit] = EscalationRule.Not(UserDecision),
        [AttentionCode.IntegrationTipNotTested] = EscalationRule.Not(RunLevel),
        [AttentionCode.StackVerificationFailed] = EscalationRule.Not(GitHubState),
        [AttentionCode.PullRequestsClosedUnmerged] = EscalationRule.Not(UserDecision),
        [AttentionCode.TrunkMissingStack] = EscalationRule.Not(GitHubState),

        [AttentionCode.InternalInconsistency] = EscalationRule.Not("An internal error of WebDevLoop; there is nothing for an agent to repair."),
        [AttentionCode.Unclassified] = EscalationRule.Not("A row from an older version without a reason code."),
    };

    /// <summary>Every code has a rule, so adding a code without deciding whether it escalates fails the tests.</summary>
    public static EscalationRule For(AttentionCode code) =>
        Rules.TryGetValue(code, out EscalationRule? rule) ? rule : EscalationRule.Not("No escalation rule is defined for this code.");

    public static IReadOnlyCollection<AttentionCode> Escalating { get; } = [.. Rules.Where(rule => rule.Value.Escalates).Select(rule => rule.Key)];

    public static IReadOnlyCollection<AttentionCode> Defined => Rules.Keys.ToArray();
}
