using WebDevLoop.Core.Domain;

namespace WebDevLoop.Core.Orchestration.Results;

/// <param name="evidence">The quoted code the finding refers to.</param>
/// <param name="rule">The cited standard (file and rule) or principle.</param>
public sealed record CodingStandardsFinding : Finding
{
    public CodingStandardsFinding(
        CodingStandardsSeverity severity,
        string file,
        int? line,
        string evidence,
        string rule,
        string description,
        string recommendation)
    {
        Severity = severity;
        File = ReportGuard.RequireText(file, nameof(file));
        Line = ReportGuard.OptionalLine(line, nameof(line));
        Evidence = ReportGuard.RequireText(evidence, nameof(evidence));
        Rule = ReportGuard.RequireText(rule, nameof(rule));
        Description = ReportGuard.RequireText(description, nameof(description));
        Recommendation = ReportGuard.RequireText(recommendation, nameof(recommendation));
    }

    public override FindingAxis Axis => FindingAxis.CodingStandards;

    public override string Title => ReportGuard.Headline(Description);

    public CodingStandardsSeverity Severity { get; }

    public string File { get; }

    public int? Line { get; }

    public string Evidence { get; }

    public string Rule { get; }

    public string Description { get; }

    public string Recommendation { get; }
}
