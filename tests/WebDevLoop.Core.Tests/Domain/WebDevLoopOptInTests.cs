using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Ports;

namespace WebDevLoop.Core.Tests.Domain;

public sealed class WebDevLoopOptInTests
{
    [Theory]
    [InlineData(IssueState.Open, "webdevloop", true)]
    [InlineData(IssueState.Open, "WEBDEVLOOP", true)]
    [InlineData(IssueState.Open, "WebDevLoop", true)]
    [InlineData(IssueState.Closed, "webdevloop", true)]
    [InlineData(IssueState.Open, null, false)]
    [InlineData(IssueState.Closed, null, false)]
    [InlineData(IssueState.Open, "ready-for-agent", false)]
    [InlineData(IssueState.Open, "webdevloop-extra", false)]
    [InlineData(IssueState.Open, " webdevloop ", false)]
    public void opt_in_requires_the_exact_label_ignoring_case_independently_of_issue_state(
        IssueState state, string? label, bool expected)
    {
        var issue = new IssueSnapshot(new IssueRef("acme", "widgets", 1), "Issue", "", state, [])
        {
            Labels = label is null ? [] : ["needs-triage", label],
        };

        Assert.Equal(expected, issue.HasOptIn());
    }

    [Theory]
    [InlineData(IssueState.Open, "webdevloop", true)]
    [InlineData(IssueState.Open, "WEBDEVLOOP", true)]
    [InlineData(IssueState.Open, "WebDevLoop", true)]
    [InlineData(IssueState.Closed, "webdevloop", false)]
    [InlineData(IssueState.Closed, "WEBDEVLOOP", false)]
    [InlineData(IssueState.Open, null, false)]
    [InlineData(IssueState.Closed, null, false)]
    [InlineData(IssueState.Open, "ready-for-agent", false)]
    [InlineData(IssueState.Open, "webdevloop-extra", false)]
    [InlineData(IssueState.Open, " webdevloop ", false)]
    public void work_selection_requires_an_open_independently_opted_in_issue(
        IssueState state, string? label, bool expected)
    {
        var issue = new IssueSnapshot(new IssueRef("acme", "widgets", 1), "Issue", "", state, [])
        {
            Labels = label is null ? [] : ["needs-triage", label],
        };

        Assert.Equal(expected, issue.IsOpenAndOptedIn());
    }
}
