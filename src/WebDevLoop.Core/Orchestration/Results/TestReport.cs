namespace WebDevLoop.Core.Orchestration.Results;

/// <summary>Tester result for the integrated app.</summary>
public sealed record TestReport : AgentReport
{
    public TestReport(TestVerdict verdict, string summary, IReadOnlyList<TestScenario> scenarios, IReadOnlyList<TestIssue> issues)
        : base(summary)
    {
        if (verdict == TestVerdict.Blocked)
        {
            ReportGuard.RequireText(summary, nameof(summary));
        }

        Verdict = verdict;
        Scenarios = ReportGuard.RequireList(scenarios, nameof(scenarios));
        Issues = ReportGuard.RequireFindings(issues, expected: verdict == TestVerdict.IssuesFound, nameof(issues), verdict);
    }

    public TestVerdict Verdict { get; }

    public IReadOnlyList<TestScenario> Scenarios { get; }

    public IReadOnlyList<TestIssue> Issues { get; }
}
