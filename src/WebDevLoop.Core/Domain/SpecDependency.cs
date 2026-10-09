namespace WebDevLoop.Core.Domain;

/// <summary>Mirrors a native GitHub blocking relationship between spec issues.</summary>
public sealed class SpecDependency
{
    private SpecDependency()
    {
    }

    public long Id { get; private set; }

    public RunId BlockedSpecRunId { get; private set; }

    public RunId? BlockingSpecRunId { get; private set; }

    public IssueRef? ExternalBlockingIssue { get; private set; }

    public DependencySource Source { get; private set; }

    public SpecDependencyMode ModeAtStart { get; private set; }

    public static SpecDependency OnSpecRun(RunId blocked, RunId blocking, SpecDependencyMode modeAtStart)
    {
        if (blocked == blocking)
        {
            throw new DependencyCycleException($"Spec run '{blocked}' cannot depend on itself.");
        }

        return new()
        {
            BlockedSpecRunId = blocked,
            BlockingSpecRunId = blocking,
            ModeAtStart = modeAtStart,
        };
    }

    public static SpecDependency OnExternalIssue(RunId blocked, IssueRef blockingIssue, SpecDependencyMode modeAtStart) => new()
    {
        BlockedSpecRunId = blocked,
        ExternalBlockingIssue = blockingIssue,
        ModeAtStart = modeAtStart,
    };
}
