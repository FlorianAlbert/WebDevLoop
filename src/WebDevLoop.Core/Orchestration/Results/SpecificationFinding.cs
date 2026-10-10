using WebDevLoop.Core.Domain;

namespace WebDevLoop.Core.Orchestration.Results;

/// <param name="specReference">The quoted specification line (for out-of-scope behaviour the closest line or "not requested").</param>
/// <param name="file">Where the behaviour is implemented, or where missing behaviour is expected.</param>
/// <param name="description">Expected versus actual behaviour.</param>
public sealed record SpecificationFinding : Finding
{
    public SpecificationFinding(
        SpecificationFindingKind kind,
        string specReference,
        string file,
        int? line,
        string description,
        string recommendation,
        string? id = null,
        IReadOnlyList<string>? blockedBy = null,
        string? title = null)
        : base(id, blockedBy)
    {
        Kind = kind;
        SpecReference = ReportGuard.RequireText(specReference, nameof(specReference));
        File = ReportGuard.RequireText(file, nameof(file));
        Line = ReportGuard.OptionalLine(line, nameof(line));
        Description = ReportGuard.RequireText(description, nameof(description));
        Recommendation = ReportGuard.RequireText(recommendation, nameof(recommendation));
        Title = ReportGuard.Headline(title, Description);
    }

    public override FindingAxis Axis => FindingAxis.Specification;

    public override string Title { get; }

    public SpecificationFindingKind Kind { get; }

    public string SpecReference { get; }

    public string File { get; }

    public int? Line { get; }

    public string Description { get; }

    public string Recommendation { get; }
}
