using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Orchestration.Results;

namespace WebDevLoop.Core.Tests.Ports;

public sealed class ResultContractTests
{
    private static readonly CommitSha Sha = new(new string('a', 40));
    private static readonly CommandResult DotnetTest = new("dotnet test", "42 passed");

    private static readonly CodingStandardsFinding StandardsFinding = new(
        CodingStandardsSeverity.Blocking, "src/Parser.cs", 42, "if (x == null) return;", "CONTRIBUTING.md#null-handling",
        "Null input is silently ignored.\nCallers never learn about bad input.", "Throw ArgumentNullException.");

    private static readonly SpecificationFinding SpecFinding = new(
        SpecificationFindingKind.Missing, "> Users can export as CSV", "src/Export/ExportController.cs", null,
        "CSV export is missing; only JSON is offered.", "Add a CSV endpoint.");

    private static readonly TestIssue Issue = new(
        "Saving an empty title crashes the page", TestIssueSeverity.Major, "> Titles are required",
        ["Open /todos", "Click Save with an empty title"], "Validation message", "Unhandled error page", ["evidence/save.png"]);

    [Fact]
    public void clean_review_rejects_findings()
    {
        Assert.Throws<InvalidAgentReportException>(() =>
            new ReviewReport(FindingAxis.CodingStandards, ReviewVerdict.Clean, "looks fine", [StandardsFinding]));
    }

    [Fact]
    public void issues_found_review_requires_findings()
    {
        Assert.Throws<InvalidAgentReportException>(() => ReviewReport.IssuesFound(FindingAxis.Specification, "problems", []));
    }

    [Fact]
    public void review_findings_must_belong_to_the_report_axis()
    {
        Assert.Throws<InvalidAgentReportException>(() => ReviewReport.IssuesFound(FindingAxis.Specification, "x", [StandardsFinding]));
        Assert.Throws<InvalidAgentReportException>(() => ReviewReport.Clean(FindingAxis.Testing, "ok"));
    }

    [Fact]
    public void valid_reviews_keep_axis_verdict_summary_and_findings()
    {
        ReviewReport issues = ReviewReport.IssuesFound(FindingAxis.Specification, "1 gap", [SpecFinding]);

        Assert.Equal(ReviewVerdict.Clean, ReviewReport.Clean(FindingAxis.CodingStandards, "ok").Verdict);
        Assert.Equal(FindingAxis.Specification, issues.Axis);
        Assert.Equal("1 gap", issues.Summary);
        Assert.Equal([SpecFinding], issues.Findings);
    }

    [Fact]
    public void review_findings_derive_a_ticket_title_from_the_first_line_of_their_description()
    {
        Assert.Equal("Null input is silently ignored.", StandardsFinding.Title);
        Assert.Equal(FindingAxis.CodingStandards, StandardsFinding.Axis);
        Assert.Equal(FindingAxis.Specification, SpecFinding.Axis);
        Assert.Equal(FindingAxis.Testing, Issue.Axis);
    }

    [Fact]
    public void coding_standards_finding_requires_location_evidence_rule_description_and_recommendation()
    {
        Assert.Throws<InvalidAgentReportException>(() => new CodingStandardsFinding(CodingStandardsSeverity.Judgement, " ", 1, "code", "SRP", "d", "r"));
        Assert.Throws<InvalidAgentReportException>(() => new CodingStandardsFinding(CodingStandardsSeverity.Judgement, "a.cs", 0, "code", "SRP", "d", "r"));
        Assert.Throws<InvalidAgentReportException>(() => new CodingStandardsFinding(CodingStandardsSeverity.Judgement, "a.cs", 1, "", "SRP", "d", "r"));
        Assert.Throws<InvalidAgentReportException>(() => new CodingStandardsFinding(CodingStandardsSeverity.Judgement, "a.cs", 1, "code", "", "d", "r"));
        Assert.Throws<InvalidAgentReportException>(() => new CodingStandardsFinding(CodingStandardsSeverity.Judgement, "a.cs", 1, "code", "SRP", "", "r"));
        Assert.Throws<InvalidAgentReportException>(() => new CodingStandardsFinding(CodingStandardsSeverity.Judgement, "a.cs", 1, "code", "SRP", "d", ""));
    }

    [Fact]
    public void specification_finding_requires_spec_reference_location_description_and_recommendation()
    {
        Assert.Throws<InvalidAgentReportException>(() => new SpecificationFinding(SpecificationFindingKind.Incorrect, "", "a.cs", null, "d", "r"));
        Assert.Throws<InvalidAgentReportException>(() => new SpecificationFinding(SpecificationFindingKind.Incorrect, "> s", "", null, "d", "r"));
        Assert.Throws<InvalidAgentReportException>(() => new SpecificationFinding(SpecificationFindingKind.Incorrect, "> s", "a.cs", null, " ", "r"));
        Assert.Throws<InvalidAgentReportException>(() => new SpecificationFinding(SpecificationFindingKind.Incorrect, "> s", "a.cs", null, "d", " "));
    }

    [Fact]
    public void test_issue_requires_title_reproduction_expected_and_actual()
    {
        Assert.Throws<InvalidAgentReportException>(() => new TestIssue("", TestIssueSeverity.Minor, null, ["step"], "e", "a", []));
        Assert.Throws<InvalidAgentReportException>(() => new TestIssue("t", TestIssueSeverity.Minor, null, [], "e", "a", []));
        Assert.Throws<InvalidAgentReportException>(() => new TestIssue("t", TestIssueSeverity.Minor, null, ["step"], "", "a", []));
        Assert.Throws<InvalidAgentReportException>(() => new TestIssue("t", TestIssueSeverity.Minor, null, ["step"], "e", "", []));
        Assert.Throws<InvalidAgentReportException>(() => new TestIssue("t", TestIssueSeverity.Minor, null, ["step"], "e", "a", null!));
    }

    [Fact]
    public void pass_test_report_rejects_issues_and_issues_found_requires_them()
    {
        Assert.Throws<InvalidAgentReportException>(() => new TestReport(TestVerdict.Pass, "ok", [], [Issue]));
        Assert.Throws<InvalidAgentReportException>(() => new TestReport(TestVerdict.IssuesFound, "broken", [], []));
        Assert.Equal([Issue], new TestReport(TestVerdict.IssuesFound, "broken", [], [Issue]).Issues);
    }

    [Fact]
    public void blocked_test_report_needs_a_reason_and_no_issues()
    {
        Assert.Throws<InvalidAgentReportException>(() => new TestReport(TestVerdict.Blocked, " ", [], []));
        Assert.Throws<InvalidAgentReportException>(() => new TestReport(TestVerdict.Blocked, "port busy", [], [Issue]));
        Assert.Equal(TestVerdict.Blocked, new TestReport(TestVerdict.Blocked, "run instructions fail: missing env var", [], []).Verdict);
    }

    [Fact]
    public void test_scenarios_need_a_name()
    {
        Assert.Throws<InvalidAgentReportException>(() => new TestScenario("", TestScenarioOutcome.Passed));
        Assert.Throws<InvalidAgentReportException>(() => new TestReport(TestVerdict.Pass, "ok", null!, []));
    }

    [Fact]
    public void completed_implementation_requires_a_head_commit_and_blocked_requires_a_reason()
    {
        Assert.Throws<InvalidAgentReportException>(() => new ImplementationReport(ReportStatus.Completed, null, "done", [], [], []));
        Assert.Throws<InvalidAgentReportException>(() => ImplementationReport.Completed(default, "done"));
        Assert.Throws<InvalidAgentReportException>(() => ImplementationReport.Blocked(""));
        Assert.Null(ImplementationReport.Blocked("spec contradicts itself").HeadCommitSha);
    }

    [Fact]
    public void implementation_report_keeps_tests_addressed_findings_and_follow_ups()
    {
        var addressed = new AddressedFinding("F1", "Now throws ArgumentNullException.");
        var report = new ImplementationReport(ReportStatus.Completed, Sha, "fixed", [DotnetTest], [addressed], ["Parser is slow"]);

        Assert.Equal(Sha, report.HeadCommitSha);
        Assert.Equal([DotnetTest], report.Tests);
        Assert.Equal([addressed], report.AddressedFindings);
        Assert.Equal(["Parser is slow"], report.FollowUps);
    }

    [Fact]
    public void implementation_report_lists_must_be_present_and_well_formed()
    {
        Assert.Throws<InvalidAgentReportException>(() => new ImplementationReport(ReportStatus.Completed, Sha, "x", null!, [], []));
        Assert.Throws<InvalidAgentReportException>(() => new ImplementationReport(ReportStatus.Completed, Sha, "x", [], [], [" "]));
        Assert.Throws<InvalidAgentReportException>(() => new AddressedFinding("", "fixed"));
        Assert.Throws<InvalidAgentReportException>(() => new AddressedFinding("F1", ""));
        Assert.Throws<InvalidAgentReportException>(() => new CommandResult("", "ok"));
    }

    [Fact]
    public void resolved_conflict_requires_a_head_commit_and_resolved_files_and_blocked_requires_a_reason()
    {
        Assert.Throws<InvalidAgentReportException>(() => new ConflictResolutionReport(ConflictResolutionStatus.Resolved, null, ["a.cs"], "merged", []));
        Assert.Throws<InvalidAgentReportException>(() => new ConflictResolutionReport(ConflictResolutionStatus.Resolved, Sha, [], "merged", []));
        Assert.Throws<InvalidAgentReportException>(() => new ConflictResolutionReport(ConflictResolutionStatus.Blocked, null, [], " ", []));
        Assert.Equal(["a.cs"], new ConflictResolutionReport(ConflictResolutionStatus.Resolved, Sha, ["a.cs"], "merged", [DotnetTest]).ResolvedFiles);
    }

    [Fact]
    public void completed_exploration_requires_notes_files_and_blocked_requires_a_reason()
    {
        Assert.Throws<InvalidAgentReportException>(() => new ExplorationReport(ReportStatus.Completed, "summary", []));
        Assert.Throws<InvalidAgentReportException>(() => new ExplorationReport(ReportStatus.Completed, "summary", [""]));
        Assert.Throws<InvalidAgentReportException>(() => new ExplorationReport(ReportStatus.Blocked, " ", []));
        Assert.Equal(["/work/notes/run1/architecture.md"], new ExplorationReport(ReportStatus.Completed, "s", ["/work/notes/run1/architecture.md"]).NotesFiles);
    }
}
