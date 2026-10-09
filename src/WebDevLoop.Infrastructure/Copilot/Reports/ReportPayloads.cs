using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Orchestration.Results;

namespace WebDevLoop.Infrastructure.Copilot.Reports;

// Wire shapes of the report tools. Property names are serialized in snake_case and mirror the report contracts the prompt
// templates describe; parameters with defaults are optional in the schema. Domain constructors do the validation.

internal sealed record CommandResultPayload(string Command, string Result)
{
    public CommandResult ToDomain() => new(Command, Result);
}

internal sealed record ExplorationPayload(ReportStatus Status, string Summary, IReadOnlyList<string> NotesFiles)
{
    public ExplorationReport ToDomain() => new(Status, Summary, NotesFiles);
}

internal sealed record AddressedFindingPayload(string FindingId, string Response)
{
    public AddressedFinding ToDomain() => new(FindingId, Response);
}

internal sealed record ImplementationPayload(
    ReportStatus Status,
    string? HeadCommitSha,
    string Summary,
    IReadOnlyList<CommandResultPayload> Tests,
    IReadOnlyList<AddressedFindingPayload> AddressedFindings,
    IReadOnlyList<string> FollowUps)
{
    public ImplementationReport ToDomain() => new(
        Status,
        ReportPayloads.Commit(HeadCommitSha),
        Summary,
        Tests.Select(test => test.ToDomain()).ToArray(),
        AddressedFindings.Select(finding => finding.ToDomain()).ToArray(),
        FollowUps);
}

internal sealed record CodingStandardsFindingPayload(
    CodingStandardsSeverity Severity,
    string File,
    int? Line,
    string Evidence,
    string Rule,
    string Description,
    string Recommendation)
{
    public CodingStandardsFinding ToDomain() => new(Severity, File, Line, Evidence, Rule, Description, Recommendation);
}

internal sealed record SpecificationFindingPayload(
    SpecificationFindingKind Kind,
    string SpecReference,
    string File,
    int? Line,
    string Description,
    string Recommendation)
{
    public SpecificationFinding ToDomain() => new(Kind, SpecReference, File, Line, Description, Recommendation);
}

internal sealed record CodingStandardsReviewPayload(
    FindingAxis Axis,
    ReviewVerdict Verdict,
    string Summary,
    IReadOnlyList<CodingStandardsFindingPayload> Findings)
{
    public ReviewReport ToDomain() => new(Axis, Verdict, Summary, Findings.Select(finding => (Finding)finding.ToDomain()).ToArray());
}

internal sealed record SpecificationReviewPayload(
    FindingAxis Axis,
    ReviewVerdict Verdict,
    string Summary,
    IReadOnlyList<SpecificationFindingPayload> Findings)
{
    public ReviewReport ToDomain() => new(Axis, Verdict, Summary, Findings.Select(finding => (Finding)finding.ToDomain()).ToArray());
}

internal sealed record ConflictResolutionPayload(
    ConflictResolutionStatus Status,
    string? HeadCommitSha,
    IReadOnlyList<string> ResolvedFiles,
    string Summary,
    IReadOnlyList<CommandResultPayload> Tests)
{
    public ConflictResolutionReport ToDomain() => new(
        Status,
        ReportPayloads.Commit(HeadCommitSha),
        ResolvedFiles,
        Summary,
        Tests.Select(test => test.ToDomain()).ToArray());
}

internal sealed record TestScenarioPayload(string Name, TestScenarioOutcome Outcome, string? Notes = null)
{
    public TestScenario ToDomain() => new(Name, Outcome, Notes);
}

internal sealed record TestIssuePayload(
    string Title,
    TestIssueSeverity Severity,
    string? SpecReference,
    IReadOnlyList<string> StepsToReproduce,
    string Expected,
    string Actual,
    IReadOnlyList<string> Evidence)
{
    public TestIssue ToDomain() => new(Title, Severity, SpecReference, StepsToReproduce, Expected, Actual, Evidence);
}

internal sealed record TestPayload(
    TestVerdict Verdict,
    string Summary,
    IReadOnlyList<TestScenarioPayload> Scenarios,
    IReadOnlyList<TestIssuePayload> Issues)
{
    public TestReport ToDomain() => new(
        Verdict,
        Summary,
        Scenarios.Select(scenario => scenario.ToDomain()).ToArray(),
        Issues.Select(issue => issue.ToDomain()).ToArray());
}

internal static class ReportPayloads
{
    /// <exception cref="InvalidAgentReportException">The value is not a full commit SHA.</exception>
    public static CommitSha? Commit(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        try
        {
            return new CommitSha(value.Trim());
        }
        catch (ArgumentException exception)
        {
            throw new InvalidAgentReportException($"'head_commit_sha': {exception.Message}");
        }
    }
}
