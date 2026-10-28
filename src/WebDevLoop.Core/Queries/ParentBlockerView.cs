using WebDevLoop.Core.Domain;

namespace WebDevLoop.Core.Queries;

/// <summary>A current native blocker enriched for display. Run and status are null when no run of the blocking issue exists in this queue.</summary>
/// <param name="Issue">The blocking issue as <c>owner/repo#number</c>.</param>
/// <param name="IsSatisfied">The blocking GitHub issue is closed, independently of any run or pull request.</param>
public sealed record ParentBlockerView(
    string? BlockingSpecRunId,
    string Issue,
    int IssueNumber,
    string? Title,
    SpecRunStatus? Status,
    bool IsSatisfied);
