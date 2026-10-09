namespace WebDevLoop.Core.Orchestration.Control;

/// <param name="IntegrationGateTimeout">How long aborting an integrating ticket waits for the repository's merge lock before reporting a conflict.</param>
public sealed record RunControlOptions(TimeSpan IntegrationGateTimeout)
{
    public static RunControlOptions Default { get; } = new(TimeSpan.FromSeconds(5));
}
