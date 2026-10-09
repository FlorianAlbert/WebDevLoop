using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Queries;

namespace WebDevLoop.Web.Tests.Components.Support;

internal static class Views
{
    public static readonly DateTimeOffset Now = new(2026, 10, 9, 12, 0, 0, TimeSpan.Zero);

    public static SpecRunView Spec(
        string id = "run-1",
        SpecRunStatus status = SpecRunStatus.Running,
        string title = "Add billing",
        SpecDependencyMode? mode = null) => new(
        id, 1, 42, title, status, 1, "main", $"integration/{id}", "base111", "tip222", mode, 0, 0, Now, Now, null, null, null);

    public static TicketRunView Ticket(
        string id,
        int issue,
        TicketRunStatus status,
        string runId = "run-1",
        int reviewIteration = 0,
        int? pullRequest = null,
        params string[] blockedBy) => new(
        id, runId, issue, $"Ticket {issue}", status, 1, reviewIteration, $"ticket/{runId}/{id}", $"/work/{id}", null, null,
        pullRequest, pullRequest is null ? null : 1, blockedBy, Now, Now, null);

    public static StepRunView Step(
        string id,
        StepKind kind = StepKind.Implement,
        AgentRole? role = AgentRole.Implementer,
        StepStatus status = StepStatus.Succeeded,
        string runId = "run-1",
        string? ticketId = "t1",
        int attempt = 1,
        string? resultJson = null,
        string? model = null,
        string? reasoningEffort = null) => new(
        id, runId, ticketId, kind, role, status, attempt, "copilot-session-1", "/work/t1", "ticket/branch", Now, null, null, "hash", resultJson, null,
        model, reasoningEffort);

    public static StackLayerView Layer(int position, string ticketId, int pullRequest) => new(
        position, ticketId, $"stack/run-1/{ticketId}", position == 1 ? "integration/run-1" : $"stack/run-1/prev", "abc1234", pullRequest, 100 + position, true, null);
}
