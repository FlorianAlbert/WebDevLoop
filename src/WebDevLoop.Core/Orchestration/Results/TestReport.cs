namespace WebDevLoop.Core.Orchestration.Results;

/// <summary>Tester result for the integrated app; failures carry findings on the testing axis.</summary>
public sealed record TestReport : AgentReport
{
    public TestReport(TestVerdict verdict, IReadOnlyList<Finding> findings, string summary)
        : base(summary)
    {
        if (verdict == TestVerdict.CouldNotRun)
        {
            ReportGuard.RequireText(summary, nameof(summary));
        }

        Verdict = verdict;
        Findings = ReportGuard.RequireFindings(findings, expected: verdict == TestVerdict.Failed, verdict.ToString());
    }

    public TestVerdict Verdict { get; }

    public IReadOnlyList<Finding> Findings { get; }
}
