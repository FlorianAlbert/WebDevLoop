using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Orchestration.Findings;
using WebDevLoop.Core.Orchestration.Results;

namespace WebDevLoop.Core.Tests.Orchestration.Findings;

public sealed class FindingFingerprintsTests
{
    private static readonly RunId Run = new("run-1");

    private static SpecificationFinding Spec(
        string description = "Errors are not logged.",
        string file = "src/Feature.cs",
        int? line = 12,
        string specReference = "Errors must be logged.") =>
        new(SpecificationFindingKind.Missing, specReference, file, line, description, "Log errors through ILogger.");

    private static CodingStandardsFinding Standards(string evidence = "var delay = 42;") =>
        new(CodingStandardsSeverity.Blocking, "src/Feature.cs", 12, evidence, "CONTRIBUTING.md: no magic values", "Magic number 42.", "Name the constant.");

    private static TestIssue Issue(params string[] steps) =>
        new("Saving fails", TestIssueSeverity.Major, null, steps, "Saved", "Error 500", []);

    [Fact]
    public void Fingerprint_names_run_source_step_and_axis()
    {
        FindingFingerprint fingerprint = FindingFingerprints.Compute(Run, StepKind.ParentReview, Spec());

        Assert.StartsWith("run-1/parent-review/specification/", fingerprint.Value, StringComparison.Ordinal);
    }

    [Fact]
    public void Formatting_differences_do_not_change_the_fingerprint()
    {
        FindingFingerprint original = FindingFingerprints.Compute(Run, StepKind.ParentReview, Spec());
        FindingFingerprint reformatted = FindingFingerprints.Compute(
            Run,
            StepKind.ParentReview,
            Spec(description: "  errors ARE not \t logged. ", file: @".\src\Feature.cs", specReference: "Errors  must be\tlogged."));

        Assert.Equal(original, reformatted);
    }

    /// <summary>Each row differs from <see cref="Spec"/>'s defaults in title, file, line, reproduction, or axis.</summary>
    public static TheoryData<Finding> DifferentFindings => new()
    {
        Spec(description: "Errors are swallowed."),
        Spec(file: "src/Other.cs"),
        Spec(line: 13),
        Spec(specReference: "Warnings must be logged."),
        Standards(),
    };

    [Theory]
    [MemberData(nameof(DifferentFindings))]
    public void Title_location_reproduction_and_axis_distinguish_findings(Finding other) =>
        Assert.NotEqual(
            FindingFingerprints.Compute(Run, StepKind.ParentReview, Spec()),
            FindingFingerprints.Compute(Run, StepKind.ParentReview, other));

    [Fact]
    public void Run_and_source_step_distinguish_findings()
    {
        FindingFingerprint parentReview = FindingFingerprints.Compute(Run, StepKind.ParentReview, Spec());

        Assert.NotEqual(parentReview, FindingFingerprints.Compute(new RunId("run-2"), StepKind.ParentReview, Spec()));
        Assert.NotEqual(parentReview, FindingFingerprints.Compute(Run, StepKind.Test, Spec()));
    }

    [Fact]
    public void Coding_standards_evidence_and_test_reproduction_steps_are_part_of_the_fingerprint()
    {
        Assert.NotEqual(
            FindingFingerprints.Compute(Run, StepKind.ParentReview, Standards()),
            FindingFingerprints.Compute(Run, StepKind.ParentReview, Standards(evidence: "var delay = 43;")));
        Assert.NotEqual(
            FindingFingerprints.Compute(Run, StepKind.Test, Issue("Open /items", "Click Save")),
            FindingFingerprints.Compute(Run, StepKind.Test, Issue("Open /items", "Press Enter")));
        Assert.StartsWith("run-1/test/testing/", FindingFingerprints.Compute(Run, StepKind.Test, Issue("Open /items")).Value, StringComparison.Ordinal);
    }
}
