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
    public void Ticket_title_is_the_reported_summary_and_the_full_text_stays_in_the_body()
    {
        const string description = "The required review diff is empty because both sides resolve to commit 62e1ebac9ff6f1a5f1b6b555c709 and nothing differs between them.";
        var finding = new SpecificationFinding(
            SpecificationFindingKind.Missing, "> subtract", "src/Calc.cs", null, description, "Implement it.", title: "Subtract is not implemented");

        FindingIssueDraft draft = Draft(StepKind.ParentReview, finding);

        Assert.Equal("Subtract is not implemented", draft.Title);
        Assert.Contains(description, draft.Body, StringComparison.Ordinal);
    }

    [Fact]
    public void Ticket_title_without_a_summary_is_cut_at_a_word_boundary()
    {
        const string description = "The required review diff is empty because both sides resolve to commit 62e1ebac9ff6f1a5f1b6b555c709 and nothing differs between them";
        var finding = new CodingStandardsFinding(
            CodingStandardsSeverity.Blocking, "src/A.cs", 1, "x", "rule", description, "Fix.");

        FindingIssueDraft draft = Draft(StepKind.ParentReview, finding);

        Assert.True(draft.Title.Length <= 100);
        Assert.EndsWith("…", draft.Title, StringComparison.Ordinal);
        Assert.StartsWith(draft.Title.TrimEnd('…'), description, StringComparison.Ordinal);
        Assert.Equal(' ', description[draft.Title.Length - 1]);
        Assert.Contains(description, draft.Body, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Short problem.", "Short problem.")]
    [InlineData("First sentence. Second sentence follows.", "First sentence.")]
    [InlineData("First line\nsecond line", "First line")]
    public void Ticket_title_without_a_summary_is_the_first_sentence_or_line(string description, string expected)
    {
        var finding = new SpecificationFinding(SpecificationFindingKind.Incorrect, "> x", "src/A.cs", null, description, "Fix.");

        Assert.Equal(expected, finding.Title);
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
