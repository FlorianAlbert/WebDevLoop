using WebDevLoop.Infrastructure.GitHub.Stacks;

namespace WebDevLoop.Infrastructure.Tests.GitHub.Stacks;

public sealed class ProcessGhCommandRunnerTests
{
    [Fact]
    public async Task Captures_exit_code_and_output_of_the_configured_executable()
    {
        var runner = new ProcessGhCommandRunner("git");

        GhCommandResult result = await runner.RunAsync(["--version"], new Dictionary<string, string>(), CancellationToken.None);

        Assert.Equal(0, result.ExitCode);
        Assert.StartsWith("git version", result.StandardOutput);
    }

    [Fact]
    public async Task Reports_a_failing_exit_code_and_standard_error()
    {
        var runner = new ProcessGhCommandRunner("git");

        GhCommandResult result = await runner.RunAsync(["no-such-subcommand"], new Dictionary<string, string>(), CancellationToken.None);

        Assert.NotEqual(0, result.ExitCode);
        Assert.NotEmpty(result.StandardError);
    }
}
