using WebDevLoop.Core.Domain;

namespace WebDevLoop.Core.Orchestration.Findings;

/// <summary>
/// Derives blocking relations between finding tickets from the dependencies the reporter stated: a finding's
/// <c>blocked_by</c> names the <c>id</c>s of findings in the same report (the same reviewer or tester step). Findings
/// without <c>blocked_by</c> stay independent; unknown ids are ignored, as the report contracts already reject them.
/// </summary>
public static class FindingDependencyPlanner
{
    /// <param name="findings">All reported findings in report order, keyed by fingerprint; repeated findings share a fingerprint.</param>
    public static IReadOnlyList<DependencyEdge<FindingFingerprint>> Plan(IReadOnlyList<(FindingFingerprint Fingerprint, SourcedFinding Source)> findings)
    {
        ArgumentNullException.ThrowIfNull(findings);
        var byReportedId = new Dictionary<(StepRunId Step, string Id), FindingFingerprint>();
        foreach ((FindingFingerprint fingerprint, SourcedFinding source) in findings)
        {
            if (source.Finding.Id is { } id)
            {
                byReportedId.TryAdd((source.SourceStepRunId, id.ToUpperInvariant()), fingerprint);
            }
        }

        var edges = new List<DependencyEdge<FindingFingerprint>>();
        foreach ((FindingFingerprint blocked, SourcedFinding source) in findings)
        {
            foreach (string blockerId in source.Finding.BlockedBy)
            {
                if (byReportedId.TryGetValue((source.SourceStepRunId, blockerId.ToUpperInvariant()), out FindingFingerprint blocking) && blocking != blocked)
                {
                    var edge = new DependencyEdge<FindingFingerprint>(blocked, blocking);
                    if (!edges.Contains(edge))
                    {
                        edges.Add(edge);
                    }
                }
            }
        }

        return edges;
    }
}
