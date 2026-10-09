using System.Globalization;
using System.Text;
using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Orchestration.Results;
using WebDevLoop.Core.Ports;

namespace WebDevLoop.Core.Orchestration.Findings;

/// <summary>
/// Renders a finding as a self-contained ticket (title and Markdown body) an implementer can work on without the agent
/// that reported it. The GitHub adapter appends the hidden fingerprint marker to the body.
/// </summary>
public static class FindingIssueDrafts
{
    private const char Backtick = '`';
    private const int MinimumFenceLength = 3;

    public static FindingIssueDraft Create(SpecRun spec, SourcedFinding sourced, FindingFingerprint fingerprint)
    {
        ArgumentNullException.ThrowIfNull(spec);
        ArgumentNullException.ThrowIfNull(sourced);
        var body = new StringBuilder();
        body.AppendLine(CultureInfo.InvariantCulture, $"{Origin(sourced)} of parent spec #{spec.ParentIssue.Number} (WebDevLoop run `{spec.Id}`, step `{sourced.SourceStepRunId}`).");
        body.AppendLine();
        switch (sourced.Finding)
        {
            case CodingStandardsFinding standards:
                AppendStandards(body, standards);
                break;
            case SpecificationFinding specification:
                AppendSpecification(body, specification);
                break;
            case TestIssue issue:
                AppendTestIssue(body, issue);
                break;
            default:
                throw new ArgumentException($"{sourced.Finding.GetType().Name} cannot become a finding ticket.", nameof(sourced));
        }

        return new FindingIssueDraft(spec.ParentIssue, sourced.Finding.Title, body.ToString().TrimEnd(), fingerprint);
    }

    private static string Origin(SourcedFinding sourced) => (sourced.SourceKind, sourced.Finding.Axis) switch
    {
        (StepKind.ParentReview, FindingAxis.CodingStandards) => "Coding-standards finding of the final parent-spec review",
        (StepKind.ParentReview, _) => "Specification finding of the final parent-spec review",
        _ => "Issue found by the tester",
    };

    private static void AppendStandards(StringBuilder body, CodingStandardsFinding finding)
    {
        AppendField(body, "Severity", finding.Severity.ToString());
        AppendField(body, "Standard", finding.Rule);
        AppendField(body, "Location", Location(finding.File, finding.Line));
        AppendSection(body, "Problem", finding.Description);
        body.AppendLine("### Evidence").AppendLine();
        AppendCode(body, finding.Evidence);
        AppendSection(body, "Recommendation", finding.Recommendation);
    }

    private static void AppendSpecification(StringBuilder body, SpecificationFinding finding)
    {
        AppendField(body, "Kind", finding.Kind.ToString());
        AppendField(body, "Location", Location(finding.File, finding.Line));
        body.AppendLine().AppendLine("### Specification").AppendLine();
        foreach (string line in finding.SpecReference.Split('\n'))
        {
            body.AppendLine(CultureInfo.InvariantCulture, $"> {line.TrimEnd()}");
        }

        body.AppendLine();
        AppendSection(body, "Expected versus actual", finding.Description);
        AppendSection(body, "Recommendation", finding.Recommendation);
    }

    private static void AppendTestIssue(StringBuilder body, TestIssue issue)
    {
        AppendField(body, "Severity", issue.Severity.ToString());
        if (issue.SpecReference is { } reference)
        {
            AppendField(body, "Specification", reference);
        }

        body.AppendLine().AppendLine("### Steps to reproduce").AppendLine();
        for (int index = 0; index < issue.StepsToReproduce.Count; index++)
        {
            body.AppendLine(CultureInfo.InvariantCulture, $"{index + 1}. {issue.StepsToReproduce[index]}");
        }

        body.AppendLine();
        AppendSection(body, "Expected", issue.Expected);
        AppendSection(body, "Actual", issue.Actual);
        if (issue.Evidence.Count > 0)
        {
            body.AppendLine("### Evidence").AppendLine();
            foreach (string evidence in issue.Evidence)
            {
                body.AppendLine(CultureInfo.InvariantCulture, $"- {evidence}");
            }

            body.AppendLine();
        }
    }

    private static void AppendField(StringBuilder body, string name, string value) =>
        body.AppendLine(CultureInfo.InvariantCulture, $"- **{name}:** {value}");

    private static void AppendSection(StringBuilder body, string heading, string text) =>
        body.AppendLine().AppendLine(CultureInfo.InvariantCulture, $"### {heading}").AppendLine().AppendLine(text.Trim()).AppendLine();

    /// <summary>The fence is longer than any backtick run in the code, so quoted code can never close it.</summary>
    private static void AppendCode(StringBuilder body, string code)
    {
        int longestRun = 0;
        int run = 0;
        foreach (char character in code)
        {
            run = character == Backtick ? run + 1 : 0;
            longestRun = Math.Max(longestRun, run);
        }

        string fence = new(Backtick, Math.Max(MinimumFenceLength, longestRun + 1));
        body.AppendLine(fence).AppendLine(code.TrimEnd()).AppendLine(fence);
    }

    private static string Location(string file, int? line) =>
        line is { } number ? string.Create(CultureInfo.InvariantCulture, $"`{file}:{number}`") : $"`{file}`";
}
