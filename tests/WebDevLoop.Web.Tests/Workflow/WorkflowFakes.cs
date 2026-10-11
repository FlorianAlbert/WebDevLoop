using WebDevLoop.Core.Agents;
using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Ports;
using WebDevLoop.Infrastructure.GitHub.Auth;
using WebDevLoop.Infrastructure.Prerequisites;

namespace WebDevLoop.Web.Tests.Workflow;

/// <summary>No Copilot runtimes: the scripted runner never leases one, so maintenance has nothing to evict or refresh.</summary>
internal sealed class IdleCopilotRuntimePool : ICopilotRuntimePool
{
    private int _maintenancePasses;

    public int MaintenancePasses => Volatile.Read(ref _maintenancePasses);

    public Task<CopilotRuntimeLease> AcquireAsync(CopilotAuthIdentity identity, CancellationToken cancellationToken) =>
        throw new NotSupportedException("The scripted agent runner does not lease Copilot runtimes.");

    public Task<CopilotRuntimeKey> ReplaceAsync(CopilotRuntimeKey stale, CancellationToken cancellationToken) =>
        throw new NotSupportedException("The scripted agent runner does not lease Copilot runtimes.");

    public Task<IReadOnlyList<CopilotRuntimeKey>> RefreshExpiringAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<CopilotRuntimeKey>>([]);

    public Task<IReadOnlyList<CopilotRuntimeKey>> EvictIdleAsync(CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _maintenancePasses);
        return Task.FromResult<IReadOnlyList<CopilotRuntimeKey>>([]);
    }
}

/// <summary>The tester's app is "started" instantly on the reserved port; nothing real is launched.</summary>
internal sealed class InstantTestTargets : ITestTargetRunner
{
    public Task<TestTarget?> ReserveAsync(RunId specRunId, TestPortRange portRange, CancellationToken cancellationToken) =>
        Task.FromResult<TestTarget?>(new TestTarget(specRunId, portRange.Start, new Uri($"http://127.0.0.1:{portRange.Start}/"), new Dictionary<string, string>()));

    public Task<TestTargetReadiness> WaitForReadyAsync(TestTarget target, TimeSpan timeout, CancellationToken cancellationToken) =>
        Task.FromResult(TestTargetReadiness.Ready);

    public Task<TestTargetStopResult> StopAsync(TestTarget target, CancellationToken cancellationToken) =>
        Task.FromResult(new TestTargetStopResult(0));
}

/// <summary>Prerequisite results under test control; every check passes unless told otherwise.</summary>
internal sealed class ScriptedPrerequisites : IPrerequisiteValidator, IPrerequisiteCheck
{
    public static readonly PrerequisiteCheck Passed = new("Git CLI", PrerequisiteStatus.Passed, "git 2.50");

    public PrerequisiteCheck[] Checks { get; set; } = [Passed];

    public string Name => "Scripted prerequisites";

    public Task<PrerequisiteCheck> RunAsync(CancellationToken cancellationToken) =>
        Task.FromResult(Checks.FirstOrDefault(check => check.Status == PrerequisiteStatus.Failed) ?? Passed);

    public Task<PrerequisiteReport> ValidateAsync(CancellationToken cancellationToken) => Task.FromResult(new PrerequisiteReport(Checks));
}

/// <summary>Sign-in at the GitHub boundary; no credentials or live GitHub access are needed by workflow scenarios.</summary>
internal sealed class ScriptedSignInState : IGitHubSignInState
{
    public string? Login { get; set; } = "test-user";

    public GitHubSignInStatus Status => new(true, Login, null, null);

    public event Action? Changed
    {
        add { }
        remove { }
    }
}
