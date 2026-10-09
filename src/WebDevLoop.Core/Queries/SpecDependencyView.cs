using WebDevLoop.Core.Domain;

namespace WebDevLoop.Core.Queries;

/// <summary>A spec issue that blocks a spec run. Run, title, and status are null when no run of the blocking issue exists in this app.</summary>
/// <param name="Issue">The blocking issue as <c>owner/repo#number</c>.</param>
/// <param name="IsSatisfied">The blocking spec's stack is merged into trunk.</param>
public sealed record SpecDependencyView(
    string? BlockingSpecRunId,
    string Issue,
    int IssueNumber,
    string? Title,
    SpecRunStatus? Status,
    bool IsSatisfied);
