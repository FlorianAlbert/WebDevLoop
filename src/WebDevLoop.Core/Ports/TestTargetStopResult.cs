namespace WebDevLoop.Core.Ports;

/// <param name="TerminatedProcessCount">Leftover processes killed (zero when the tester already stopped the app).</param>
public sealed record TestTargetStopResult(int TerminatedProcessCount);
