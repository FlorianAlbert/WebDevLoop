using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Orchestration.Results;

namespace WebDevLoop.Core.Orchestration.Findings;

/// <summary>
/// Normalized identity of a parent-review or tester finding: <c>&lt;run&gt;/&lt;source step&gt;/&lt;axis&gt;/&lt;hash&gt;</c>, where the
/// hash covers the finding's title, location, and reproduction. Case, whitespace, and path-separator differences do not
/// change it, so a finding reported again in a later cycle maps to the ticket that was already created for it.
/// </summary>
public static partial class FindingFingerprints
{
    private const char Separator = '/';
    private const string NoLocation = "-";

    public static FindingFingerprint Compute(RunId specRunId, StepKind sourceKind, Finding finding)
    {
        ArgumentNullException.ThrowIfNull(finding);
        (string location, string reproduction) = Describe(finding);
        string content = string.Join('\n', Normalize(finding.Title), Normalize(location), Normalize(reproduction));
        string hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(content)));
        return new FindingFingerprint(string.Join(Separator, specRunId.Value, SourceName(sourceKind), AxisName(finding.Axis), hash));
    }

    private static (string Location, string Reproduction) Describe(Finding finding) => finding switch
    {
        CodingStandardsFinding standards => (Location(standards.File, standards.Line), $"{standards.Rule}\n{standards.Evidence}"),
        SpecificationFinding specification => (Location(specification.File, specification.Line), $"{specification.Kind}\n{specification.SpecReference}"),
        TestIssue issue => (issue.SpecReference ?? NoLocation, string.Join('\n', issue.StepsToReproduce)),
        _ => throw new ArgumentException($"{finding.GetType().Name} cannot be fingerprinted.", nameof(finding)),
    };

    private static string Location(string file, int? line)
    {
        string path = FindingPaths.Normalize(file);
        return line is { } number ? string.Create(CultureInfo.InvariantCulture, $"{path}:{number}") : path;
    }

    private static string Normalize(string text) => Whitespace().Replace(text.Trim(), " ").ToLowerInvariant();

    private static string SourceName(StepKind kind) => kind switch
    {
        StepKind.ParentReview => "parent-review",
        StepKind.Test => "test",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Only parent-spec reviews and tests create finding tickets."),
    };

    private static string AxisName(FindingAxis axis) => axis switch
    {
        FindingAxis.CodingStandards => "coding-standards",
        FindingAxis.Specification => "specification",
        FindingAxis.Testing => "testing",
        _ => throw new ArgumentOutOfRangeException(nameof(axis), axis, null),
    };

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}
