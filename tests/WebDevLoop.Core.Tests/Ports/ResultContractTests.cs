using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Orchestration.Results;

namespace WebDevLoop.Core.Tests.Ports;

public sealed class ResultContractTests
{
    private static readonly CommitSha Sha = new(new string('a', 40));
    private static readonly Finding SampleFinding = new("Null check missing", "Parser crashes on empty input.", "src/Parser.cs:42");

    [Fact]
    public void clean_review_rejects_findings()
    {
        Assert.Throws<InvalidAgentReportException>(() =>
            new ReviewReport(FindingAxis.CodingStandards, ReviewVerdict.Clean, [SampleFinding], "looks fine"));
    }

    [Fact]
    public void issues_found_review_requires_findings()
    {
        Assert.Throws<InvalidAgentReportException>(() =>
            new ReviewReport(FindingAxis.Specification, ReviewVerdict.IssuesFound, [], "problems"));
        Assert.Throws<InvalidAgentReportException>(() => ReviewReport.IssuesFound(FindingAxis.Specification, [], "problems"));
    }

    [Fact]
    public void review_on_the_testing_axis_is_rejected()
    {
        Assert.Throws<InvalidAgentReportException>(() => ReviewReport.Clean(FindingAxis.Testing, "ok"));
    }

    [Fact]
    public void valid_reviews_keep_axis_verdict_and_findings()
    {
        ReviewReport clean = ReviewReport.Clean(FindingAxis.CodingStandards, "ok");
        ReviewReport issues = ReviewReport.IssuesFound(FindingAxis.Specification, [SampleFinding], "1 issue");

        Assert.Equal(ReviewVerdict.Clean, clean.Verdict);
        Assert.Empty(clean.Findings);
        Assert.Equal(FindingAxis.Specification, issues.Axis);
        Assert.Equal(ReviewVerdict.IssuesFound, issues.Verdict);
        Assert.Equal([SampleFinding], issues.Findings);
    }

    [Theory]
    [InlineData("", "details")]
    [InlineData("  ", "details")]
    [InlineData("title", "")]
    public void finding_requires_title_and_details(string title, string details)
    {
        Assert.Throws<InvalidAgentReportException>(() => new Finding(title, details));
    }

    [Fact]
    public void passed_test_report_rejects_findings_and_failed_requires_them()
    {
        Assert.Throws<InvalidAgentReportException>(() => new TestReport(TestVerdict.Passed, [SampleFinding], "ok"));
        Assert.Throws<InvalidAgentReportException>(() => new TestReport(TestVerdict.Failed, [], "broken"));
        Assert.Equal([SampleFinding], new TestReport(TestVerdict.Failed, [SampleFinding], "broken").Findings);
    }

    [Fact]
    public void could_not_run_test_report_needs_a_reason_and_no_findings()
    {
        Assert.Throws<InvalidAgentReportException>(() => new TestReport(TestVerdict.CouldNotRun, [], " "));
        Assert.Throws<InvalidAgentReportException>(() => new TestReport(TestVerdict.CouldNotRun, [SampleFinding], "port busy"));
    }

    [Fact]
    public void implemented_report_requires_a_commit_and_blocked_requires_a_reason()
    {
        Assert.Throws<InvalidAgentReportException>(() => ImplementationReport.Implemented(default, "done"));
        Assert.Throws<InvalidAgentReportException>(() => ImplementationReport.Blocked(""));

        ImplementationReport implemented = ImplementationReport.Implemented(Sha, "done");
        Assert.Equal(ImplementationOutcome.Implemented, implemented.Outcome);
        Assert.Equal(Sha, implemented.Commit);
        Assert.Null(ImplementationReport.Blocked("spec contradicts itself").Commit);
    }

    [Fact]
    public void resolved_conflict_requires_a_commit_and_unresolvable_requires_a_reason()
    {
        Assert.Throws<InvalidAgentReportException>(() => ConflictResolutionReport.Resolved(default, "merged"));
        Assert.Throws<InvalidAgentReportException>(() => ConflictResolutionReport.Unresolvable(" "));
        Assert.Equal(Sha, ConflictResolutionReport.Resolved(Sha, "merged").Commit);
    }

    [Fact]
    public void exploration_report_requires_a_notes_path()
    {
        Assert.Throws<InvalidAgentReportException>(() => new ExplorationReport("summary", ""));
    }
}
