namespace WebDevLoop.Core.Queries;

/// <summary>An eligible parent spec, not yet in an unfinished run.</summary>
public sealed record SpecCandidateView(int IssueNumber, string Title);
