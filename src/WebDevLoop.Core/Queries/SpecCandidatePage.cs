namespace WebDevLoop.Core.Queries;

/// <summary>NextPage is the next GitHub source page; it must be reused with the same search and filter.</summary>
public sealed record SpecCandidatePage(IReadOnlyList<SpecCandidateView> Items, int? NextPage);
