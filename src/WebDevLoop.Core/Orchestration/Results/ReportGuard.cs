using WebDevLoop.Core.Domain;

namespace WebDevLoop.Core.Orchestration.Results;

internal static class ReportGuard
{
    public static string RequireText(string? value, string field) =>
        string.IsNullOrWhiteSpace(value) ? throw new InvalidAgentReportException($"'{field}' must not be blank.") : value;

    public static CommitSha RequireCommit(CommitSha commit, string field) =>
        commit.Value is null ? throw new InvalidAgentReportException($"'{field}' must be a commit SHA.") : commit;

    public static IReadOnlyList<Finding> RequireFindings(IReadOnlyList<Finding>? findings, bool expected, string verdict)
    {
        if (findings is null || findings.Any(finding => finding is null))
        {
            throw new InvalidAgentReportException("Findings must be a list without null entries.");
        }

        return expected switch
        {
            true when findings.Count == 0 => throw new InvalidAgentReportException($"Verdict '{verdict}' requires at least one finding."),
            false when findings.Count > 0 => throw new InvalidAgentReportException($"Verdict '{verdict}' must not carry findings."),
            _ => findings,
        };
    }
}
