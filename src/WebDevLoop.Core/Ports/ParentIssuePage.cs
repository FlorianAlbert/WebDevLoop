namespace WebDevLoop.Core.Ports;

/// <summary>A page of open, opted-in issues without a parent; pull requests are excluded.</summary>
public sealed record ParentIssuePage(IReadOnlyList<ParentIssueSummary> Issues, int? NextPage)
{
    /// <summary>Smaller than bulk GitHub reads (100) to bound per-candidate eligibility lookups.</summary>
    public const int PageSize = 30;
}
