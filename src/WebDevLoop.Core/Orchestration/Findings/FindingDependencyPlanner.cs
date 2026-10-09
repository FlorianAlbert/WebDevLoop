using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Orchestration.Results;

namespace WebDevLoop.Core.Orchestration.Findings;

/// <summary>
/// Derives blocking relations between finding tickets from the findings' structured locations: findings in the same file
/// would edit the same code in parallel worktrees and conflict at squash time, so each one is blocked by the previous
/// finding (in report order) in that file. Findings without a file location (tester issues) stay independent.
/// </summary>
public static class FindingDependencyPlanner
{
    /// <param name="findings">Distinct findings in report order, keyed by fingerprint.</param>
    public static IReadOnlyList<DependencyEdge<FindingFingerprint>> Plan(IReadOnlyList<(FindingFingerprint Fingerprint, Finding Finding)> findings)
    {
        ArgumentNullException.ThrowIfNull(findings);
        var lastInFile = new Dictionary<string, FindingFingerprint>(StringComparer.OrdinalIgnoreCase);
        var edges = new List<DependencyEdge<FindingFingerprint>>();
        foreach ((FindingFingerprint fingerprint, Finding finding) in findings)
        {
            if (FileOf(finding) is not { } file)
            {
                continue;
            }

            if (lastInFile.TryGetValue(file, out FindingFingerprint previous))
            {
                edges.Add(new DependencyEdge<FindingFingerprint>(fingerprint, previous));
            }

            lastInFile[file] = fingerprint;
        }

        return edges;
    }

    private static string? FileOf(Finding finding) => finding switch
    {
        CodingStandardsFinding standards => FindingPaths.Normalize(standards.File),
        SpecificationFinding specification => FindingPaths.Normalize(specification.File),
        _ => null,
    };
}
