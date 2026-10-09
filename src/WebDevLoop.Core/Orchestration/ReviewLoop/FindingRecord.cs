using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Orchestration.Results;

namespace WebDevLoop.Core.Orchestration.ReviewLoop;

/// <summary>Flat JSON form of a review finding (persisted with the review step and sent to the implementer).</summary>
/// <param name="Id">App-assigned id the implementer refers to in <c>addressed_findings</c>; only set in fix prompts.</param>
/// <param name="ReportedId">The reviewer-assigned id, kept so <paramref name="BlockedBy"/> still resolves after a restart.</param>
/// <param name="BlockedBy">Reviewer-assigned ids of the findings in the same report that block this one; null when none.</param>
internal sealed record FindingRecord(
    string? Id,
    FindingAxis Axis,
    string Title,
    string File,
    int? Line,
    string Description,
    string Recommendation,
    CodingStandardsSeverity? Severity = null,
    string? Rule = null,
    string? Evidence = null,
    SpecificationFindingKind? Kind = null,
    string? SpecReference = null,
    string? ReportedId = null,
    IReadOnlyList<string>? BlockedBy = null)
{
    public static FindingRecord From(Finding finding, string? id = null) => finding switch
    {
        CodingStandardsFinding standards => new(
            id, standards.Axis, standards.Title, standards.File, standards.Line, standards.Description, standards.Recommendation,
            Severity: standards.Severity, Rule: standards.Rule, Evidence: standards.Evidence,
            ReportedId: standards.Id, BlockedBy: Dependencies(standards)),
        SpecificationFinding specification => new(
            id, specification.Axis, specification.Title, specification.File, specification.Line, specification.Description, specification.Recommendation,
            Kind: specification.Kind, SpecReference: specification.SpecReference,
            ReportedId: specification.Id, BlockedBy: Dependencies(specification)),
        _ => throw new ArgumentException($"{finding.GetType().Name} is not a review finding.", nameof(finding)),
    };

    public Finding ToDomain() => Axis switch
    {
        FindingAxis.CodingStandards => new CodingStandardsFinding(Require(Severity), File, Line, Require(Evidence), Require(Rule), Description, Recommendation, ReportedId, BlockedBy),
        FindingAxis.Specification => new SpecificationFinding(Require(Kind), Require(SpecReference), File, Line, Description, Recommendation, ReportedId, BlockedBy),
        _ => throw new InvalidAgentReportException($"'{Axis}' is not a review axis."),
    };

    private static IReadOnlyList<string>? Dependencies(Finding finding) => finding.BlockedBy.Count == 0 ? null : finding.BlockedBy;

    private static T Require<T>(T? value)
        where T : class => value ?? throw new InvalidAgentReportException("A persisted review finding is incomplete.");

    private static T Require<T>(T? value)
        where T : struct => value ?? throw new InvalidAgentReportException("A persisted review finding is incomplete.");
}
