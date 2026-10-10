namespace WebDevLoop.Core.Agents;

/// <summary>Names of the terminal custom tools through which agents report structured results.</summary>
public static class AgentReportTools
{
    public const string Exploration = "report_exploration";
    public const string Implementation = "report_implementation";
    public const string Review = "report_review";
    public const string ConflictResolution = "report_conflict_resolution";
    public const string Test = "report_test";
    public const string Troubleshooting = "report_troubleshooting";
}
