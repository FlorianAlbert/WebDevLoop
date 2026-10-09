namespace WebDevLoop.Web.Components.Layout;

/// <summary>Navigation targets shared by the page groups (dashboard/repositories/queue, runs, settings/health).</summary>
public static class UiRoutes
{
    public const string Dashboard = "/";
    public const string Repositories = "/repositories";
    public const string Queue = "/queue";
    public const string Settings = "/settings";
    public const string Health = "/health";
    public const string GitHub = "/github";

    public static string Run(string specRunId) => $"/runs/{Uri.EscapeDataString(specRunId)}";
}
