using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Orchestration.Attention;

namespace WebDevLoop.Core.Tests.Orchestration.Attention;

public sealed class TroubleshooterEscalationTests
{
    public static TheoryData<AttentionCode> AllCodes => [.. Enum.GetValues<AttentionCode>()];

    [Theory]
    [MemberData(nameof(AllCodes))]
    public void every_reason_code_has_an_explicit_decision_with_a_rationale(AttentionCode code)
    {
        Assert.Contains(code, TroubleshooterEscalation.Defined);

        EscalationRule rule = TroubleshooterEscalation.For(code);

        Assert.False(string.IsNullOrWhiteSpace(rule.Rationale));
        Assert.Equal(rule.Escalates, rule.Check is not null);
    }

    [Theory]
    [InlineData(AttentionCode.ImplementationFailed)]
    [InlineData(AttentionCode.ImplementerBlocked)]
    [InlineData(AttentionCode.WorktreeNotClean)]
    [InlineData(AttentionCode.TicketBranchNotBasedOnIntegration)]
    [InlineData(AttentionCode.ReportedCommitMismatch)]
    [InlineData(AttentionCode.ReviewFailed)]
    [InlineData(AttentionCode.FixFailed)]
    [InlineData(AttentionCode.IntegrationFailed)]
    [InlineData(AttentionCode.MergeConflictUnresolved)]
    [InlineData(AttentionCode.InterruptedRepeatedly)]
    public void unexpected_states_and_failed_agents_escalate(AttentionCode code) => Assert.True(TroubleshooterEscalation.For(code).Escalates);

    [Theory]
    [InlineData(AttentionCode.PromptNotRenderable)]
    [InlineData(AttentionCode.RepositoryNotRegistered)]
    [InlineData(AttentionCode.BaseBranchMissing)]
    [InlineData(AttentionCode.ReviewIterationsExhausted)]
    [InlineData(AttentionCode.ParentReviewCycleLimit)]
    [InlineData(AttentionCode.TestCycleLimit)]
    [InlineData(AttentionCode.IntegrationTemporaryFailure)]
    [InlineData(AttentionCode.IntegrationBranchMoved)]
    [InlineData(AttentionCode.IntegrationPushRejected)]
    [InlineData(AttentionCode.StackBranchExists)]
    [InlineData(AttentionCode.PullRequestNotOpen)]
    [InlineData(AttentionCode.PullRequestStackChanged)]
    [InlineData(AttentionCode.PullRequestsClosedUnmerged)]
    [InlineData(AttentionCode.ForeignPullRequest)]
    [InlineData(AttentionCode.InternalInconsistency)]
    public void deterministic_codes_user_decisions_and_github_state_never_escalate(AttentionCode code) => Assert.False(TroubleshooterEscalation.For(code).Escalates);

    [Fact]
    public void a_code_without_a_rule_does_not_escalate() => Assert.False(TroubleshooterEscalation.For((AttentionCode)9999).Escalates);

    [Fact]
    public void the_escalating_set_matches_the_rules() =>
        Assert.Equal(
            [.. Enum.GetValues<AttentionCode>().Where(code => TroubleshooterEscalation.For(code).Escalates).Order()],
            [.. TroubleshooterEscalation.Escalating.Order()]);
}
