namespace WebDevLoop.Core.Orchestration.Results;

/// <summary>A workflow or edge case the tester exercised.</summary>
public sealed record TestScenario
{
    public TestScenario(string name, TestScenarioOutcome outcome, string? notes = null)
    {
        Name = ReportGuard.RequireText(name, nameof(name));
        Outcome = outcome;
        Notes = ReportGuard.OptionalText(notes);
    }

    public string Name { get; }

    public TestScenarioOutcome Outcome { get; }

    public string? Notes { get; }
}
