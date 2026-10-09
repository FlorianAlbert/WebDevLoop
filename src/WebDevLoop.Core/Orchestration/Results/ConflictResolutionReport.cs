using WebDevLoop.Core.Domain;

namespace WebDevLoop.Core.Orchestration.Results;

/// <summary>Conflict-resolver result; <see cref="HeadCommitSha"/> is the local resolution commit the app re-squashes from.</summary>
public sealed record ConflictResolutionReport : AgentReport
{
    public ConflictResolutionReport(
        ConflictResolutionStatus status,
        CommitSha? headCommitSha,
        IReadOnlyList<string> resolvedFiles,
        string summary,
        IReadOnlyList<CommandResult> tests)
        : base(summary)
    {
        if (status == ConflictResolutionStatus.Resolved)
        {
            ReportGuard.RequireCommit(headCommitSha, nameof(headCommitSha));
        }
        else
        {
            ReportGuard.RequireText(summary, nameof(summary));
        }

        Status = status;
        HeadCommitSha = headCommitSha is { Value: not null } ? headCommitSha : null;
        ResolvedFiles = ReportGuard.RequireTextList(resolvedFiles, nameof(resolvedFiles), requireAny: status == ConflictResolutionStatus.Resolved);
        Tests = ReportGuard.RequireList(tests, nameof(tests));
    }

    public ConflictResolutionStatus Status { get; }

    public CommitSha? HeadCommitSha { get; }

    public IReadOnlyList<string> ResolvedFiles { get; }

    public IReadOnlyList<CommandResult> Tests { get; }
}
