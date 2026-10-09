namespace WebDevLoop.Core.Orchestration.Results;

public sealed record ExplorationReport : AgentReport
{
    /// <param name="notesFiles">Notes files written or updated inside the app-allocated notes directory.</param>
    public ExplorationReport(ReportStatus status, string summary, IReadOnlyList<string> notesFiles)
        : base(summary)
    {
        if (status == ReportStatus.Blocked)
        {
            ReportGuard.RequireText(summary, nameof(summary));
        }

        Status = status;
        NotesFiles = ReportGuard.RequireTextList(notesFiles, nameof(notesFiles), requireAny: status == ReportStatus.Completed);
    }

    public ReportStatus Status { get; }

    public IReadOnlyList<string> NotesFiles { get; }
}
