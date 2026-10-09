using WebDevLoop.Core.Agents;
using WebDevLoop.Core.Orchestration.Recovery.AgentSteps;

namespace WebDevLoop.Core.Tests.Orchestration.Recovery.AgentSteps;

public sealed class CopilotRuntimeRecoveryTests
{
    private static readonly CopilotAuthIdentity Idle = new(CopilotAuthKind.GitHubAppInstallation, "idle-tester-runtime");
    private static readonly CopilotAuthIdentity Busy = new(CopilotAuthKind.GitHubAppInstallation, "busy-runtime");

    private readonly AgentStepRecoveryFixture _fixture = new();

    [Fact]
    public async Task idle_runtimes_are_evicted_before_expiring_runtimes_are_refreshed()
    {
        await (await _fixture.Runtimes.AcquireAsync(Idle, AgentStepRecoveryFixture.Token)).DisposeAsync();
        await using CopilotRuntimeLease busy = await _fixture.Runtimes.AcquireAsync(Busy, AgentStepRecoveryFixture.Token);
        _fixture.Clock.Advance(TimeSpan.FromMinutes(56));

        AgentStepRecoveryReport report = await _fixture.RecoverAsync();

        Assert.Equal([Idle], report.EvictedRuntimes.Select(key => key.Identity));
        Assert.Equal([Busy], report.RefreshedRuntimes.Select(key => key.Identity));
    }

    [Fact]
    public async Task a_runtime_used_within_the_idle_timeout_is_kept()
    {
        await (await _fixture.Runtimes.AcquireAsync(Idle, AgentStepRecoveryFixture.Token)).DisposeAsync();
        _fixture.Clock.Advance(AgentStepRecoveryFixture.RuntimeIdleTimeout - TimeSpan.FromSeconds(1));

        AgentStepRecoveryReport report = await _fixture.RecoverAsync();

        Assert.Empty(report.EvictedRuntimes);
    }
}
