namespace WebDevLoop.Infrastructure.Prerequisites;

public enum ReadinessMode
{
    /// <summary>Prerequisites failed or were not evaluated yet: only health/prerequisite pages may serve, no workflow runs.</summary>
    DiagnosticOnly,

    Operational,
}
