using System.Globalization;
using System.Text.Json;
using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Orchestration.Results;

namespace WebDevLoop.Core.Orchestration.ReviewLoop;

/// <summary>
/// Renders <c>{review_findings_json}</c>: the findings to fix, each with an app-assigned id (<c>cs-1</c>, <c>spec-1</c>, …).
/// The reviewer's own ids and dependencies only matter for parent-review tickets and are left out.
/// </summary>
internal static class ReviewFindingsJson
{
    private static readonly IReadOnlyDictionary<FindingAxis, string> IdPrefixes = new Dictionary<FindingAxis, string>
    {
        [FindingAxis.CodingStandards] = "cs",
        [FindingAxis.Specification] = "spec",
    };

    public static string Render(IEnumerable<Finding> findings)
    {
        FindingRecord[] records = findings
            .GroupBy(finding => finding.Axis)
            .OrderBy(group => group.Key)
            .SelectMany(group => group.Select((finding, index) => FindingRecord.From(finding, IdFor(group.Key, index)) with { ReportedId = null, BlockedBy = null }))
            .ToArray();
        return JsonSerializer.Serialize(records, ReviewJson.Indented);
    }

    private static string IdFor(FindingAxis axis, int index) =>
        string.Create(CultureInfo.InvariantCulture, $"{IdPrefixes[axis]}-{index + 1}");
}
