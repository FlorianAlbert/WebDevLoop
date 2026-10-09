using WebDevLoop.Core.Domain;

namespace WebDevLoop.Core.Orchestration.SpecQueue;

internal static class SpecIssues
{
    /// <summary>Identity by repository and number; node/database ids are optional enrichments and may be missing on either side.</summary>
    public static bool AreSame(IssueRef left, IssueRef right) =>
        left.Number == right.Number
            && string.Equals(left.Owner, right.Owner, StringComparison.OrdinalIgnoreCase)
            && string.Equals(left.Repo, right.Repo, StringComparison.OrdinalIgnoreCase);
}
