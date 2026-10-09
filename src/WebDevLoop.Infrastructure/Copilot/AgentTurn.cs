using System.Text.Json;
using WebDevLoop.Core.Agents;
using WebDevLoop.Core.Orchestration.Results;
using WebDevLoop.Infrastructure.Copilot.Reports;
using WebDevLoop.Infrastructure.Copilot.Runtime;

namespace WebDevLoop.Infrastructure.Copilot;

/// <summary>Observes one prompt turn: the first report call, error, or idle event decides how it ended.</summary>
internal sealed class AgentTurn(AgentReportTool reportTool, AgentLogForwarder log)
{
    private readonly TaskCompletionSource<TurnEnd> _end = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public Task<TurnEnd> Completion => _end.Task;

    public string OnReport(JsonElement arguments)
    {
        ReportParseResult result = reportTool.Parse(arguments);
        if (result.Report is { } report)
        {
            _end.TrySetResult(new TurnEnd.Reported(report));
            return "Report recorded. Your turn is complete.";
        }

        _end.TrySetResult(new TurnEnd.Rejected(result.Error!));
        log.Append(AgentLogKind.Error, $"{reportTool.Name} rejected: {result.Error}");
        return $"Report rejected: {result.Error}";
    }

    public void OnEvent(CopilotSessionEvent sessionEvent)
    {
        switch (sessionEvent.Kind)
        {
            case CopilotSessionEventKind.Idle:
                _end.TrySetResult(new TurnEnd.Missing());
                break;
            case CopilotSessionEventKind.Error:
                log.Append(AgentLogKind.Error, sessionEvent.Text);
                _end.TrySetResult(new TurnEnd.Errored(sessionEvent.Text, sessionEvent.IsAuthenticationFailure));
                break;
            default:
                log.Append(LogKind(sessionEvent.Kind), sessionEvent.Text);
                break;
        }
    }

    private static AgentLogKind LogKind(CopilotSessionEventKind kind) => kind switch
    {
        CopilotSessionEventKind.AssistantMessage => AgentLogKind.Assistant,
        CopilotSessionEventKind.Reasoning => AgentLogKind.Reasoning,
        CopilotSessionEventKind.ToolStarted => AgentLogKind.ToolStarted,
        CopilotSessionEventKind.ToolCompleted => AgentLogKind.ToolCompleted,
        CopilotSessionEventKind.ShellOutput => AgentLogKind.ShellOutput,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
    };
}

internal abstract record TurnEnd
{
    public sealed record Reported(AgentReport Report) : TurnEnd;

    public sealed record Rejected(string Reason) : TurnEnd;

    public sealed record Missing : TurnEnd;

    public sealed record Errored(string Message, bool IsAuthenticationFailure) : TurnEnd;
}
