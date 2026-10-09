using System.Text.Json;
using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Orchestration.Results;
using WebDevLoop.Core.Orchestration.ReviewLoop;

namespace WebDevLoop.Core.Orchestration.Completion.Testing;

/// <summary>
/// Structured result of a succeeded tester step: the test report plus the test cycle and integration tip it belongs to,
/// so a restarted runner reuses the verdict instead of testing the same tip again.
/// </summary>
internal sealed record TestStepRecord(
    int TestCycle,
    string TestedHead,
    TestVerdict Verdict,
    string Summary,
    IReadOnlyList<TestScenarioRecord> Scenarios,
    IReadOnlyList<TestIssueRecord> Issues)
{
    public static TestStepRecord From(int testCycle, CommitSha testedHead, TestReport report) => new(
        testCycle,
        testedHead.Value,
        report.Verdict,
        report.Summary,
        report.Scenarios.Select(scenario => new TestScenarioRecord(scenario.Name, scenario.Outcome, scenario.Notes)).ToArray(),
        report.Issues.Select(TestIssueRecord.From).ToArray());

    public static TestStepRecord? TryParse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<TestStepRecord>(json, ReviewJson.Options);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <returns>The latest succeeded tester step of <paramref name="testCycle"/> that tested exactly <paramref name="testedHead"/>.</returns>
    public static (StepRunId StepRunId, TestStepRecord Record)? Latest(IEnumerable<StepRun> steps, int testCycle, CommitSha testedHead) =>
        steps
            .Where(step => step is { Kind: StepKind.Test, Status: StepStatus.Succeeded, TicketRunId: null })
            .OrderByDescending(step => step.Attempt)
            .Select(step => (step.Id, Record: TryParse(step.StructuredResultJson)))
            .Where(candidate => candidate.Record is { } record && record.TestCycle == testCycle && record.TestedHead == testedHead.Value)
            .Select(candidate => ((StepRunId, TestStepRecord)?)(candidate.Id, candidate.Record!))
            .FirstOrDefault();

    public TestReport ToReport() => new(
        Verdict,
        Summary,
        Scenarios.Select(scenario => new TestScenario(scenario.Name, scenario.Outcome, scenario.Notes)).ToArray(),
        Issues.Select(issue => issue.ToDomain()).ToArray());

    public string ToJson() => JsonSerializer.Serialize(this, ReviewJson.Options);
}

internal sealed record TestScenarioRecord(string Name, TestScenarioOutcome Outcome, string? Notes);

/// <param name="Id">The tester-assigned id, kept so <paramref name="BlockedBy"/> still resolves after a restart.</param>
/// <param name="BlockedBy">Tester-assigned ids of the issues in the same report that block this one; null when none.</param>
internal sealed record TestIssueRecord(
    string Title,
    TestIssueSeverity Severity,
    string? SpecReference,
    IReadOnlyList<string> StepsToReproduce,
    string Expected,
    string Actual,
    IReadOnlyList<string> Evidence,
    string? Id = null,
    IReadOnlyList<string>? BlockedBy = null)
{
    public static TestIssueRecord From(TestIssue issue) => new(
        issue.Title,
        issue.Severity,
        issue.SpecReference,
        issue.StepsToReproduce,
        issue.Expected,
        issue.Actual,
        issue.Evidence,
        issue.Id,
        issue.BlockedBy.Count == 0 ? null : issue.BlockedBy);

    public TestIssue ToDomain() => new(Title, Severity, SpecReference, StepsToReproduce, Expected, Actual, Evidence, Id, BlockedBy);
}
