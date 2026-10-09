namespace WebDevLoop.Core.Orchestration.Completion.ReadyAndMerge;

/// <summary>
/// Starts a spec's completion in the background so event handling never waits for GitHub or Git. Implementations run
/// <see cref="SpecCompletionService.RunAsync"/> in a fresh unit-of-work scope and must return immediately. Launching the same
/// assignment twice is safe: completion acts on the spec's persisted state and every external call is idempotent.
/// </summary>
public interface ICompletionLauncher
{
    void Launch(CompletionAssignment assignment);
}
