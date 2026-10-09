namespace WebDevLoop.Core.Orchestration.Preparation;

/// <summary>
/// Starts a claimed spec's preparation in the background so event handling never waits for clones, GitHub, or the explorer.
/// Implementations run <see cref="SpecPreparationService.PrepareAsync"/> in a fresh unit-of-work scope and must return
/// immediately. Launching the same assignment twice is safe: preparation is replay-safe and moves the spec on only once.
/// </summary>
public interface IPreparationLauncher
{
    void Launch(PreparationAssignment assignment);
}
