namespace WebDevLoop.Core.Orchestration.Results;

public enum TestVerdict
{
    Passed,
    Failed,

    /// <summary>The tester could not start or reach the app; not a product finding.</summary>
    CouldNotRun,
}
