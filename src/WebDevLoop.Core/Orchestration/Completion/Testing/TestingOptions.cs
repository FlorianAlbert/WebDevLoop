namespace WebDevLoop.Core.Orchestration.Completion.Testing;

/// <param name="SkillsRoot">Directory holding the copied bundled skills (<c>{skills_root}</c>), including <c>playwright-cli</c>.</param>
public sealed record TestingOptions(string SkillsRoot)
{
    /// <summary>How long the tester may take to get the application accepting connections on the reserved port.</summary>
    public TimeSpan AppStartupTimeout { get; init; } = TimeSpan.FromMinutes(10);

    /// <summary>Added to the tester timeout for the lease expiry, so a lease only expires once its tester step is overdue.</summary>
    public TimeSpan LeaseGracePeriod { get; init; } = TimeSpan.FromMinutes(5);
}
