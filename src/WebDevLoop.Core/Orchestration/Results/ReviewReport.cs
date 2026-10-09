using WebDevLoop.Core.Domain;

namespace WebDevLoop.Core.Orchestration.Results;

/// <summary>Result of one review axis (ticket review or final parent-spec review).</summary>
public sealed record ReviewReport : AgentReport
{
    public ReviewReport(FindingAxis axis, ReviewVerdict verdict, IReadOnlyList<Finding> findings, string summary)
        : base(summary)
    {
        if (axis is not (FindingAxis.CodingStandards or FindingAxis.Specification))
        {
            throw new InvalidAgentReportException($"Reviews cover the coding-standards or specification axis, not '{axis}'.");
        }

        Axis = axis;
        Verdict = verdict;
        Findings = ReportGuard.RequireFindings(findings, expected: verdict == ReviewVerdict.IssuesFound, verdict.ToString());
    }

    public FindingAxis Axis { get; }

    public ReviewVerdict Verdict { get; }

    public IReadOnlyList<Finding> Findings { get; }

    public static ReviewReport Clean(FindingAxis axis, string summary) => new(axis, ReviewVerdict.Clean, [], summary);

    public static ReviewReport IssuesFound(FindingAxis axis, IReadOnlyList<Finding> findings, string summary) =>
        new(axis, ReviewVerdict.IssuesFound, findings, summary);
}
