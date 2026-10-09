using WebDevLoop.Core.Domain;

namespace WebDevLoop.Core.Orchestration.Results;

/// <summary>Conflict-resolver result; <see cref="Commit"/> is the local resolution commit the app re-squashes from.</summary>
public sealed record ConflictResolutionReport : AgentReport
{
    private ConflictResolutionReport(ConflictResolutionOutcome outcome, CommitSha? commit, string summary)
        : base(summary)
    {
        Outcome = outcome;
        Commit = commit;
    }

    public ConflictResolutionOutcome Outcome { get; }

    public CommitSha? Commit { get; }

    public static ConflictResolutionReport Resolved(CommitSha commit, string summary) =>
        new(ConflictResolutionOutcome.Resolved, ReportGuard.RequireCommit(commit, nameof(commit)), summary);

    public static ConflictResolutionReport Unresolvable(string reason) =>
        new(ConflictResolutionOutcome.Unresolvable, null, ReportGuard.RequireText(reason, nameof(reason)));
}
