using WebDevLoop.Core.Domain;

namespace WebDevLoop.Core.Orchestration.Results;

/// <summary>A self-contained tester issue; it becomes a ticket an implementer fixes without talking to the tester.</summary>
/// <param name="stepsToReproduce">Exact user actions starting from the app URL, in order.</param>
/// <param name="evidence">Saved screenshot/snapshot/log paths and key console or network lines.</param>
public sealed record TestIssue : Finding
{
    public TestIssue(
        string title,
        TestIssueSeverity severity,
        string? specReference,
        IReadOnlyList<string> stepsToReproduce,
        string expected,
        string actual,
        IReadOnlyList<string> evidence)
    {
        Title = ReportGuard.RequireText(title, nameof(title));
        Severity = severity;
        SpecReference = ReportGuard.OptionalText(specReference);
        StepsToReproduce = ReportGuard.RequireTextList(stepsToReproduce, nameof(stepsToReproduce), requireAny: true);
        Expected = ReportGuard.RequireText(expected, nameof(expected));
        Actual = ReportGuard.RequireText(actual, nameof(actual));
        Evidence = ReportGuard.RequireTextList(evidence, nameof(evidence));
    }

    public override FindingAxis Axis => FindingAxis.Testing;

    public override string Title { get; }

    public TestIssueSeverity Severity { get; }

    public string? SpecReference { get; }

    public IReadOnlyList<string> StepsToReproduce { get; }

    public string Expected { get; }

    public string Actual { get; }

    public IReadOnlyList<string> Evidence { get; }
}
