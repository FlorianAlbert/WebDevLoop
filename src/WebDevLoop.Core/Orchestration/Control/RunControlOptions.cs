namespace WebDevLoop.Core.Orchestration.Control;

/// <param name="IntegrationGateTimeout">
/// How long aborting an integrating ticket, or a spec with one, waits for the repository's merge lock before reporting a conflict.
/// </param>
public sealed record RunControlOptions(TimeSpan IntegrationGateTimeout)
{
    public static RunControlOptions Default { get; } = new(TimeSpan.FromSeconds(5));
}
