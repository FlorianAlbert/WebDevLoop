namespace WebDevLoop.Core.Orchestration.Completion.Testing;

/// <summary>
/// Starts a spec's tester run in the background so event handling never waits for the tester agent or GitHub.
/// Implementations run <see cref="SpecTestRunner.RunAsync"/> in a fresh unit-of-work scope and must return immediately.
/// Launching the same assignment twice is safe: tester steps and test leases are claimed once per spec.
/// </summary>
public interface ITestingLauncher
{
    void Launch(TestingAssignment assignment);
}
