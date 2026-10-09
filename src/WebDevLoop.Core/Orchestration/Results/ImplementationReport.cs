using WebDevLoop.Core.Domain;

namespace WebDevLoop.Core.Orchestration.Results;

/// <summary>
/// Implementer result. <see cref="Commit"/> is only a claim: the app validates it is the ticket branch head and contains the
/// integration tip before reviewing.
/// </summary>
public sealed record ImplementationReport : AgentReport
{
    private ImplementationReport(ImplementationOutcome outcome, CommitSha? commit, string summary)
        : base(summary)
    {
        Outcome = outcome;
        Commit = commit;
    }

    public ImplementationOutcome Outcome { get; }

    public CommitSha? Commit { get; }

    public static ImplementationReport Implemented(CommitSha commit, string summary) =>
        new(ImplementationOutcome.Implemented, ReportGuard.RequireCommit(commit, nameof(commit)), summary);

    public static ImplementationReport Blocked(string reason) =>
        new(ImplementationOutcome.Blocked, null, ReportGuard.RequireText(reason, nameof(reason)));
}
