using WebDevLoop.Core.Domain;

namespace WebDevLoop.Core.Orchestration.Results;

/// <summary>
/// Implementer result for an initial or fix turn. <see cref="HeadCommitSha"/> is only a claim: the app validates it is the
/// ticket branch head and contains the integration tip before reviewing.
/// </summary>
public sealed record ImplementationReport : AgentReport
{
    public ImplementationReport(
        ReportStatus status,
        CommitSha? headCommitSha,
        string summary,
        IReadOnlyList<CommandResult> tests,
        IReadOnlyList<AddressedFinding> addressedFindings,
        IReadOnlyList<string> followUps)
        : base(summary)
    {
        if (status == ReportStatus.Completed)
        {
            ReportGuard.RequireCommit(headCommitSha, nameof(headCommitSha));
        }
        else
        {
            ReportGuard.RequireText(summary, nameof(summary));
        }

        Status = status;
        HeadCommitSha = headCommitSha is { Value: not null } ? headCommitSha : null;
        Tests = ReportGuard.RequireList(tests, nameof(tests));
        AddressedFindings = ReportGuard.RequireList(addressedFindings, nameof(addressedFindings));
        FollowUps = ReportGuard.RequireTextList(followUps, nameof(followUps));
    }

    public ReportStatus Status { get; }

    public CommitSha? HeadCommitSha { get; }

    public IReadOnlyList<CommandResult> Tests { get; }

    /// <summary>Empty on the initial implementation.</summary>
    public IReadOnlyList<AddressedFinding> AddressedFindings { get; }

    /// <summary>Out-of-scope problems the implementer noticed.</summary>
    public IReadOnlyList<string> FollowUps { get; }

    public static ImplementationReport Completed(CommitSha headCommitSha, string summary) =>
        new(ReportStatus.Completed, headCommitSha, summary, [], [], []);

    public static ImplementationReport Blocked(string reason) => new(ReportStatus.Blocked, null, reason, [], [], []);
}
