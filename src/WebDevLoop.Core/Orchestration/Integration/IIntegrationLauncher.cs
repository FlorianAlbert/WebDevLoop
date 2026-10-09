namespace WebDevLoop.Core.Orchestration.Integration;

/// <summary>
/// Starts a ticket's integration saga in the background so event handling never waits for merges or GitHub. Implementations
/// run <see cref="IntegrationSagaRunner.RunAsync"/> in a fresh unit-of-work scope and must return immediately. Launching the
/// same assignment twice is safe: sagas run one at a time per repository and resume from their persisted checkpoint.
/// </summary>
public interface IIntegrationLauncher
{
    void Launch(IntegrationAssignment assignment);
}
