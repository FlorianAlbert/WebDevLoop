using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Orchestration.Completion.ReadyAndMerge;
using WebDevLoop.Core.Ports;
using WebDevLoop.Core.Tests.Orchestration.Completion.ReadyAndMerge;

namespace WebDevLoop.Core.Tests.Orchestration.Recovery.ExternalState;

/// <summary>
/// The no-PR completion reports the integration branch on the spec issue and only then persists <c>Completed</c>. A crash
/// in between must not post the report again when the completion is resumed.
/// </summary>
public sealed class NoPullRequestReportRecoveryTests
{
    private readonly ReadyAndMergeFixture _fixture = new();

    [Fact]
    public async Task Completion_resumed_after_a_crash_right_after_the_report_comment_does_not_post_it_again()
    {
        SpecRun spec = await _fixture.StartAsync(10, 11);
        await _fixture.IntegrateWithoutPullRequestAsync(spec, 11);
        await _fixture.MoveAsync(spec, SpecRunStatus.ParentReviewing, SpecRunStatus.Testing);
        await _fixture.PassTestsAsync(spec);
        SpecCompletionService crashing = new(
            _fixture.Store, _fixture.Store, _fixture.Store, _fixture.Store, _fixture.Store, _fixture.Store,
            _fixture.Git, _fixture.Pulls, new CrashAfterCommentIssues(_fixture.Issues), _fixture.Gate, _fixture.Store, _fixture.Store, _fixture.Clock);
        Assert.Equal(CompletionOutcome.Faulted, (await crashing.RunAsync(new CompletionAssignment(spec.Id), ReadyAndMergeFixture.Token)).Outcome);
        Assert.Equal(SpecRunStatus.Testing, spec.Status);

        MergeTrackingPass resumed = await _fixture.Tracking().TrackAllAsync(ReadyAndMergeFixture.Token);

        Assert.Equal(CompletionOutcome.CompletedWithoutPullRequests, resumed.Completions[spec.Id].Outcome);
        Assert.Equal(SpecRunStatus.Completed, spec.Status);
        (IssueRef issue, string body) = Assert.Single(_fixture.Issues.Comments);
        Assert.Equal(spec.ParentIssue.Number, issue.Number);
        Assert.Contains(spec.IntegrationBranch.Value, body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_report_of_another_run_of_the_same_spec_issue_does_not_suppress_this_runs_report()
    {
        SpecRun spec = await _fixture.StartAsync(10, 11);
        await _fixture.IntegrateWithoutPullRequestAsync(spec, 11);
        await _fixture.MoveAsync(spec, SpecRunStatus.ParentReviewing, SpecRunStatus.Testing);
        await _fixture.PassTestsAsync(spec);
        await _fixture.Issues.CommentAsync(spec.ParentIssue, "WebDevLoop finished spec run `run-0` on `webdevloop/run-0/integration`.", ReadyAndMergeFixture.Token);

        await _fixture.Tracking().TrackAllAsync(ReadyAndMergeFixture.Token);

        Assert.Equal(SpecRunStatus.Completed, spec.Status);
        Assert.Equal(2, _fixture.Issues.Comments.Count);
    }

    /// <summary>Posts the comment, then the process "crashes" before anything else happens.</summary>
    private sealed class CrashAfterCommentIssues(IGitHubIssues inner) : IGitHubIssues
    {
        public Task<IssueSnapshot> GetIssueAsync(IssueRef issue, CancellationToken cancellationToken) => inner.GetIssueAsync(issue, cancellationToken);

        public Task<SpecIssueGraph> GetSpecGraphAsync(IssueRef specIssue, CancellationToken cancellationToken) => inner.GetSpecGraphAsync(specIssue, cancellationToken);

        public Task<IssueSnapshot?> FindFindingIssueAsync(IssueRef specIssue, FindingFingerprint fingerprint, CancellationToken cancellationToken) =>
            inner.FindFindingIssueAsync(specIssue, fingerprint, cancellationToken);

        public Task<IssueSnapshot> CreateFindingIssueAsync(FindingIssueDraft draft, CancellationToken cancellationToken) => inner.CreateFindingIssueAsync(draft, cancellationToken);

        public Task AddSubIssueAsync(IssueRef parent, IssueRef child, CancellationToken cancellationToken) => inner.AddSubIssueAsync(parent, child, cancellationToken);

        public Task AddBlockedByAsync(IssueRef blocked, IssueRef blocking, CancellationToken cancellationToken) => inner.AddBlockedByAsync(blocked, blocking, cancellationToken);

        public async Task CommentAsync(IssueRef issue, string body, CancellationToken cancellationToken)
        {
            await inner.CommentAsync(issue, body, cancellationToken);
            throw new InvalidOperationException("Simulated crash right after commenting.");
        }

        public Task<IReadOnlyList<string>> ListCommentsAsync(IssueRef issue, CancellationToken cancellationToken) => inner.ListCommentsAsync(issue, cancellationToken);

        public Task CloseAsync(IssueRef issue, IssueCloseReason reason, CancellationToken cancellationToken) => inner.CloseAsync(issue, reason, cancellationToken);
    }
}
