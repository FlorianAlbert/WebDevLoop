namespace WebDevLoop.Infrastructure.Prerequisites;

/// <summary>Runs a short, non-interactive command to find out whether a tool is installed and working.</summary>
public interface IProcessProbe
{
    Task<ProcessProbeResult> RunAsync(string executable, IReadOnlyList<string> arguments, CancellationToken cancellationToken);
}
