using WebDevLoop.Core.Ports;
using WebDevLoop.Infrastructure.Copilot;
using WebDevLoop.Infrastructure.Skills;
using WebDevLoop.Infrastructure.Tests.Copilot.Fakes;
using WebDevLoop.Infrastructure.Tests.GitHub.Auth;

namespace WebDevLoop.Infrastructure.Tests.Copilot;

public sealed class CopilotAgentServicesTests
{
    [Fact]
    public async Task create_wires_the_sdk_backed_runner_pool_and_bundled_skills_without_starting_a_runtime()
    {
        var clock = new TestClock(DateTimeOffset.UnixEpoch);

        await using CopilotAgentServices services = CopilotAgentServices.Create(
            new CopilotTokenProviderFake(clock),
            new RecordingLogSink(),
            clock,
            new CopilotRuntimeOptions { BaseDirectory = "/data/copilot" },
            new BundledSkillsOptions());

        Assert.NotNull(services.Runner);
        Assert.Empty(await services.RuntimePool.RefreshExpiringAsync(TestContext.Current.CancellationToken));
        Assert.True(services.Skills.Validate().IsValid);
    }
}
