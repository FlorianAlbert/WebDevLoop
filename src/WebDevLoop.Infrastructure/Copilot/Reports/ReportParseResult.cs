using WebDevLoop.Core.Orchestration.Results;

namespace WebDevLoop.Infrastructure.Copilot.Reports;

/// <summary>Either a validated report or the reason the payload was rejected.</summary>
internal sealed record ReportParseResult
{
    private ReportParseResult(AgentReport? report, string? error)
    {
        Report = report;
        Error = error;
    }

    public AgentReport? Report { get; }

    public string? Error { get; }

    public static ReportParseResult Accepted(AgentReport report) => new(report, null);

    public static ReportParseResult Rejected(string error) => new(null, error);
}
