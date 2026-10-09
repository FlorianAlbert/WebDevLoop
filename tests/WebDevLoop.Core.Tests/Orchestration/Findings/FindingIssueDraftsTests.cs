using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Orchestration.Findings;
using WebDevLoop.Core.Orchestration.Results;
using WebDevLoop.Core.Ports;

namespace WebDevLoop.Core.Tests.Orchestration.Findings;

public sealed class FindingIssueDraftsTests
{
    private static readonly SpecRun Spec = SpecRun.Queue(new RunId("run-1"), 1, new IssueRef("octo", "app", 7), "Spec", "body", 1, DateTimeOffset.UnixEpoch);

    private static FindingIssueDraft Draft(StepKind kind, Finding finding) =>
        FindingIssueDrafts.Create(Spec, new SourcedFinding(kind, new StepRunId("step-1"), finding), new FindingFingerprint("fp"));

    [Fact]
    public void Coding_standards_ticket_quotes_its_evidence_in_a_fence_it_cannot_close()
    {
        var finding = new CodingStandardsFinding(
            CodingStandardsSeverity.Judgement, "src/Docs.cs", 3, "/// ```\n/// sample\n/// ```", "CONTRIBUTING.md: docs", "Doc comment embeds code.", "Use <code>.");

        FindingIssueDraft draft = Draft(StepKind.ParentReview, finding);

        Assert.Equal((Spec.ParentIssue, "Doc comment embeds code.", new FindingFingerprint("fp")), (draft.Parent, draft.Title, draft.Fingerprint));
        Assert.StartsWith("Coding-standards finding of the final parent-spec review of parent spec #7 (WebDevLoop run `run-1`, step `step-1`).", draft.Body, StringComparison.Ordinal);
        Assert.Contains("- **Standard:** CONTRIBUTING.md: docs", draft.Body, StringComparison.Ordinal);
        Assert.Contains("- **Location:** `src/Docs.cs:3`", draft.Body, StringComparison.Ordinal);
        Assert.Contains("````\n/// ```\n/// sample\n/// ```\n````", draft.Body.ReplaceLineEndings("\n"), StringComparison.Ordinal);
        Assert.Contains("### Recommendation\n\nUse <code>.", draft.Body.ReplaceLineEndings("\n"), StringComparison.Ordinal);
    }

    [Fact]
    public void Tester_ticket_lists_reproduction_steps_expected_and_actual_behaviour()
    {
        var issue = new TestIssue("Saving fails", TestIssueSeverity.Major, "Users can save items.", ["Open /items", "Click Save"], "Item saved", "Error 500", ["shots/save.png"]);

        string body = Draft(StepKind.Test, issue).Body.ReplaceLineEndings("\n");

        Assert.StartsWith("Issue found by the tester of parent spec #7", body, StringComparison.Ordinal);
        Assert.Contains("1. Open /items\n2. Click Save", body, StringComparison.Ordinal);
        Assert.Contains("### Expected\n\nItem saved", body, StringComparison.Ordinal);
        Assert.Contains("### Actual\n\nError 500", body, StringComparison.Ordinal);
        Assert.Contains("- shots/save.png", body, StringComparison.Ordinal);
    }
}
