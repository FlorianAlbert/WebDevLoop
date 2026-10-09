namespace WebDevLoop.Web.Components.Steps;

public sealed record StepReport(string? Summary, string? Verdict, int? Iteration, IReadOnlyList<ReportFinding> Findings, string PrettyJson);

/// <param name="Category">Severity of a coding-standards finding or kind of a specification finding.</param>
public sealed record ReportFinding(string Title, string? Category, string? File, int? Line, string? Description, string? Recommendation);
