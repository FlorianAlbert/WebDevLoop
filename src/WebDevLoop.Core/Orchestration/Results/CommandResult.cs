namespace WebDevLoop.Core.Orchestration.Results;

/// <summary>A build/test/lint command the agent ran and its result, as reported in <c>tests</c>.</summary>
public sealed record CommandResult
{
    public CommandResult(string command, string result)
    {
        Command = ReportGuard.RequireText(command, nameof(command));
        Result = result ?? string.Empty;
    }

    public string Command { get; }

    public string Result { get; }
}
