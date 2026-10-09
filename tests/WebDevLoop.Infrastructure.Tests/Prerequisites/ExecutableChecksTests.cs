using WebDevLoop.Core.Ports;
using WebDevLoop.Infrastructure.Prerequisites;
using static WebDevLoop.Infrastructure.Tests.Prerequisites.TestPrerequisiteOptions;

namespace WebDevLoop.Infrastructure.Tests.Prerequisites;

public sealed class ExecutableChecksTests
{
    private const string ConfiguredCli = "/opt/copilot/copilot";

    [Fact]
    public async Task installed_git_passes()
    {
        var processes = new FakeProcessProbe().Responds("git", "--version", Ok("git version 2.45.0"));

        PrerequisiteCheck result = await new GitCliCheck(Create(), processes).RunAsync(CancellationToken.None);

        Assert.Equal(PrerequisiteStatus.Passed, result.Status);
        Assert.Contains("2.45.0", result.Message);
    }

    [Theory]
    [InlineData(ProcessProbeOutcome.NotFound)]
    [InlineData(ProcessProbeOutcome.TimedOut)]
    public async Task unavailable_git_fails_because_agent_shells_need_it(ProcessProbeOutcome outcome)
    {
        var processes = new FakeProcessProbe().Responds("git", "--version", new ProcessProbeResult(outcome));

        PrerequisiteCheck result = await new GitCliCheck(Create(), processes).RunAsync(CancellationToken.None);

        Assert.Equal(PrerequisiteStatus.Failed, result.Status);
        Assert.Contains("git", result.Message);
    }

    [Fact]
    public async Task gh_with_stack_extension_passes_in_either_mode()
    {
        foreach (GhStackMode mode in Enum.GetValues<GhStackMode>())
        {
            PrerequisiteCheck result = await new GhStackCheck(Create(ghStackMode: mode), GhInstalled()).RunAsync(CancellationToken.None);

            Assert.Equal(PrerequisiteStatus.Passed, result.Status);
        }
    }

    [Fact]
    public async Task missing_gh_is_a_warning_for_rest_stack_mode()
    {
        PrerequisiteCheck result = await new GhStackCheck(Create(), new FakeProcessProbe()).RunAsync(CancellationToken.None);

        Assert.Equal(PrerequisiteStatus.Warning, result.Status);
        Assert.True(new PrerequisiteReport([result]).IsReady);
    }

    [Fact]
    public async Task missing_gh_is_a_failure_for_required_fallback_mode()
    {
        PrerequisiteCheck result = await new GhStackCheck(
            Create(ghStackMode: GhStackMode.FallbackRequired), new FakeProcessProbe()).RunAsync(CancellationToken.None);

        Assert.Equal(PrerequisiteStatus.Failed, result.Status);
        Assert.Contains("gh", result.Message);
    }

    [Theory]
    [InlineData(GhStackMode.RestWithOptionalFallback, PrerequisiteStatus.Warning)]
    [InlineData(GhStackMode.FallbackRequired, PrerequisiteStatus.Failed)]
    public async Task gh_without_the_stack_extension_follows_the_mode(GhStackMode mode, PrerequisiteStatus expected)
    {
        var processes = new FakeProcessProbe().Responds("gh", "--version", Ok("gh version 2.60.0"));

        PrerequisiteCheck result = await new GhStackCheck(Create(ghStackMode: mode), processes).RunAsync(CancellationToken.None);

        Assert.Equal(expected, result.Status);
        Assert.Contains("gh stack", result.Message);
    }

    [Fact]
    public async Task configured_copilot_cli_that_runs_passes()
    {
        var fileSystem = new FakeFileSystemProbe { Files = { ConfiguredCli } };
        var processes = new FakeProcessProbe().Responds(ConfiguredCli, "--version", Ok("1.0.18"));

        PrerequisiteCheck result = await new CopilotRuntimeCheck(Create(copilotCliPath: ConfiguredCli), fileSystem, processes)
            .RunAsync(CancellationToken.None);

        Assert.Equal(PrerequisiteStatus.Passed, result.Status);
        Assert.Contains("1.0.18", result.Message);
    }

    [Fact]
    public async Task configured_copilot_cli_that_does_not_exist_fails_without_falling_back_to_the_bundled_cli()
    {
        var fileSystem = new FakeFileSystemProbe { Files = { BundledCopilotCli } };
        var processes = new FakeProcessProbe().Responds(BundledCopilotCli, "--version", Ok());

        PrerequisiteCheck result = await new CopilotRuntimeCheck(Create(copilotCliPath: ConfiguredCli), fileSystem, processes)
            .RunAsync(CancellationToken.None);

        Assert.Equal(PrerequisiteStatus.Failed, result.Status);
        Assert.Contains(ConfiguredCli, result.Message);
        Assert.Empty(processes.Invocations);
    }

    [Fact]
    public async Task copilot_cli_that_cannot_run_fails()
    {
        var fileSystem = new FakeFileSystemProbe { Files = { ConfiguredCli } };
        var processes = new FakeProcessProbe().Responds(ConfiguredCli, "--version", new ProcessProbeResult(ProcessProbeOutcome.Completed, 126));

        PrerequisiteCheck result = await new CopilotRuntimeCheck(Create(copilotCliPath: ConfiguredCli), fileSystem, processes)
            .RunAsync(CancellationToken.None);

        Assert.Equal(PrerequisiteStatus.Failed, result.Status);
        Assert.Contains(ConfiguredCli, result.Message);
    }

    [Fact]
    public async Task bundled_copilot_cli_is_used_when_no_path_is_configured()
    {
        var fileSystem = new FakeFileSystemProbe { Files = { BundledCopilotCli } };
        var processes = new FakeProcessProbe().Responds(BundledCopilotCli, "--version", Ok("1.0.18"));

        PrerequisiteCheck result = await new CopilotRuntimeCheck(Create(), fileSystem, processes).RunAsync(CancellationToken.None);

        Assert.Equal(PrerequisiteStatus.Passed, result.Status);
    }

    [Fact]
    public async Task missing_copilot_cli_fails_and_explains_how_to_provide_it()
    {
        PrerequisiteCheck result = await new CopilotRuntimeCheck(Create(), new FakeFileSystemProbe(), new FakeProcessProbe())
            .RunAsync(CancellationToken.None);

        Assert.Equal(PrerequisiteStatus.Failed, result.Status);
        Assert.Contains(BundledCopilotCli, result.Message);
        Assert.Contains("CliPath", result.Remediation);
    }

    [Fact]
    public async Task installed_playwright_cli_passes()
    {
        var processes = new FakeProcessProbe().Responds("playwright-cli", "--version", Ok("0.1.0"));

        PrerequisiteCheck result = await new PlaywrightCliCheck(Create(), processes).RunAsync(CancellationToken.None);

        Assert.Equal(PrerequisiteStatus.Passed, result.Status);
    }

    [Fact]
    public async Task missing_playwright_cli_fails_the_tester_prerequisite()
    {
        PrerequisiteCheck result = await new PlaywrightCliCheck(Create(), new FakeProcessProbe()).RunAsync(CancellationToken.None);

        Assert.Equal(PrerequisiteStatus.Failed, result.Status);
        Assert.Contains("tester", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("npm install -g @playwright/cli", result.Remediation);
        Assert.False(new PrerequisiteReport([result]).IsReady);
    }

    private static FakeProcessProbe GhInstalled() => new FakeProcessProbe()
        .Responds("gh", "--version", Ok("gh version 2.60.0"))
        .Responds("gh", "stack --help", Ok("Manage stacked PRs"));
}
