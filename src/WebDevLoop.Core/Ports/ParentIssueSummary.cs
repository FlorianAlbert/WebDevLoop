using WebDevLoop.Core.Domain;

namespace WebDevLoop.Core.Ports;

/// <summary>Lightweight discovery results, not an eligibility decision. Relationships are read separately.</summary>
public sealed record ParentIssueSummary(IssueRef Ref, string Title);
