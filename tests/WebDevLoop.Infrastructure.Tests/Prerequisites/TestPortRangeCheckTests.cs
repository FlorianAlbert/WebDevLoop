using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Ports;
using WebDevLoop.Infrastructure.Prerequisites;

namespace WebDevLoop.Infrastructure.Tests.Prerequisites;

public sealed class TestPortRangeCheckTests
{
    [Fact]
    public async Task configured_unprivileged_range_passes()
    {
        PrerequisiteCheck result = await RunAsync(new TestPortRange(41000, 41099));

        Assert.Equal(PrerequisiteStatus.Passed, result.Status);
        Assert.Contains("41000", result.Message);
        Assert.Contains("41099", result.Message);
    }

    [Fact]
    public async Task missing_range_fails()
    {
        PrerequisiteCheck result = await new TestPortRangeCheck(OptionsWith(null)).RunAsync(CancellationToken.None);

        Assert.Equal(PrerequisiteStatus.Failed, result.Status);
    }

    [Fact]
    public async Task uninitialised_range_fails()
    {
        PrerequisiteCheck result = await RunAsync(default(TestPortRange));

        Assert.Equal(PrerequisiteStatus.Failed, result.Status);
    }

    [Fact]
    public async Task range_including_privileged_ports_warns()
    {
        PrerequisiteCheck result = await RunAsync(new TestPortRange(80, 8080));

        Assert.Equal(PrerequisiteStatus.Warning, result.Status);
    }

    private static Task<PrerequisiteCheck> RunAsync(TestPortRange range) =>
        new TestPortRangeCheck(OptionsWith(range)).RunAsync(CancellationToken.None);

    private static PrerequisiteOptions OptionsWith(TestPortRange? range) => new()
    {
        WorkspaceRoot = TestPrerequisiteOptions.WorkspaceRoot,
        GitHubAuth = new(),
        GitHubSignIn = new FakeGitHubSignInState(null),
        TestPortRange = range,
    };
}
