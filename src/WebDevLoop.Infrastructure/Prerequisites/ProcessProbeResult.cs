namespace WebDevLoop.Infrastructure.Prerequisites;

public enum ProcessProbeOutcome
{
    Completed,
    NotFound,
    TimedOut,
}

public sealed record ProcessProbeResult(ProcessProbeOutcome Outcome, int ExitCode = 0, string Output = "")
{
    public bool Succeeded => Outcome == ProcessProbeOutcome.Completed && ExitCode == 0;

    public static ProcessProbeResult NotFound { get; } = new(ProcessProbeOutcome.NotFound);

    public static ProcessProbeResult TimedOut { get; } = new(ProcessProbeOutcome.TimedOut);

    public string FailureReason => Outcome switch
    {
        ProcessProbeOutcome.NotFound => "it was not found on PATH",
        ProcessProbeOutcome.TimedOut => "it did not respond in time",
        _ => $"it exited with code {ExitCode}",
    };
}
