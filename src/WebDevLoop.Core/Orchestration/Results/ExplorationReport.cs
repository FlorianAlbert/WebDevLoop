namespace WebDevLoop.Core.Orchestration.Results;

/// <param name="notesPath">File the explorer wrote inside its app-allocated notes directory.</param>
public sealed record ExplorationReport : AgentReport
{
    public ExplorationReport(string summary, string notesPath)
        : base(summary)
    {
        NotesPath = ReportGuard.RequireText(notesPath, nameof(notesPath));
    }

    public string NotesPath { get; }
}
