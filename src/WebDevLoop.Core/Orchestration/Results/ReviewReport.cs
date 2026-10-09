using WebDevLoop.Core.Domain;

namespace WebDevLoop.Core.Orchestration.Results;

/// <summary>Result of one review axis (ticket review or final parent-spec review).</summary>
public sealed record ReviewReport : AgentReport
{
    public ReviewReport(FindingAxis axis, ReviewVerdict verdict, string summary, IReadOnlyList<Finding> findings)
        : base(summary)
    {
        if (axis is not (FindingAxis.CodingStandards or FindingAxis.Specification))
        {
            throw new InvalidAgentReportException($"Reviews cover the coding-standards or specification axis, not '{axis}'.");
        }

        Axis = axis;
        Verdict = verdict;
        Findings = ReportGuard.RequireFindings(findings, expected: verdict == ReviewVerdict.IssuesFound, nameof(findings), verdict);
        if (Findings.Any(finding => finding.Axis != axis))
        {
            throw new InvalidAgentReportException($"A {axis} review may only report {axis} findings.");
        }
    }

    public FindingAxis Axis { get; }

    public ReviewVerdict Verdict { get; }

    /// <summary><see cref="CodingStandardsFinding"/>s or <see cref="SpecificationFinding"/>s matching <see cref="Axis"/>.</summary>
    public IReadOnlyList<Finding> Findings { get; }

    public static ReviewReport Clean(FindingAxis axis, string summary) => new(axis, ReviewVerdict.Clean, summary, []);

    public static ReviewReport IssuesFound(FindingAxis axis, string summary, IReadOnlyList<Finding> findings) =>
        new(axis, ReviewVerdict.IssuesFound, summary, findings);
}
