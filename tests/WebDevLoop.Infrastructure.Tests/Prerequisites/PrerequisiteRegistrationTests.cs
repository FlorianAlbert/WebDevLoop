using Microsoft.Extensions.DependencyInjection;
using WebDevLoop.Core.Ports;
using WebDevLoop.Infrastructure.Prerequisites;
using WebDevLoop.Infrastructure.Skills;

namespace WebDevLoop.Infrastructure.Tests.Prerequisites;

public sealed class PrerequisiteRegistrationTests
{
    [Fact]
    public void registers_every_check_the_validator_and_one_shared_readiness_state()
    {
        using ServiceProvider provider = BuildProvider();

        string[] names = provider.GetServices<IPrerequisiteCheck>().Select(check => check.Name).ToArray();

        Assert.Equal(
        [
            WorkspaceRootCheck.CheckName,
            DatabaseCheck.CheckName,
            BundledSkillsCheck.CheckName,
            GitHubAuthCheck.CheckName,
            LibGit2SharpCheck.CheckName,
            GitCliCheck.CheckName,
            GhStackCheck.CheckName,
            CopilotRuntimeCheck.CheckName,
            PlaywrightCliCheck.CheckName,
            TestPortRangeCheck.CheckName,
        ],
        names);
        Assert.IsType<PrerequisiteValidator>(provider.GetRequiredService<IPrerequisiteValidator>());
        Assert.Same(provider.GetRequiredService<DiagnosticReadiness>(), provider.GetRequiredService<DiagnosticReadiness>());
    }

    [Fact]
    public async Task a_broken_environment_yields_a_report_instead_of_an_exception()
    {
        using ServiceProvider provider = BuildProvider();

        PrerequisiteReport report = await provider.GetRequiredService<IPrerequisiteValidator>().ValidateAsync(CancellationToken.None);

        Assert.Equal(10, report.Checks.Count);
        Assert.False(report.IsReady);
        Assert.Equal(ReadinessMode.DiagnosticOnly, provider.GetRequiredService<DiagnosticReadiness>().Current.Mode);
    }

    [Fact]
    public void options_from_a_factory_are_resolved_once_when_the_checks_are_first_needed()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IClock>(new FixedClock(DateTimeOffset.UnixEpoch));
        int resolved = 0;
        services.AddPrerequisites(
            _ =>
            {
                resolved++;
                return new PrerequisiteOptions { WorkspaceRoot = Path.Combine(AppContext.BaseDirectory, "test-scratch", "late-workspace"), GitHubAuth = new(), GitHubSignIn = new FakeGitHubSignInState(null) };
            },
            "Data Source=:memory:",
            new BundledSkillsCatalog(new BundledSkillsOptions { Root = Path.Combine(AppContext.BaseDirectory, "no-skills") }));
        using ServiceProvider provider = services.BuildServiceProvider();

        Assert.Equal(0, resolved);
        Assert.Equal(10, provider.GetServices<IPrerequisiteCheck>().Count());
        Assert.Equal(1, resolved);
    }

    private static ServiceProvider BuildProvider()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IClock>(new FixedClock(DateTimeOffset.UnixEpoch));
        var options = new PrerequisiteOptions
        {
            WorkspaceRoot = Path.Combine(AppContext.BaseDirectory, "test-scratch", "unused-workspace"),
            GitHubAuth = new(),
            GitHubSignIn = new FakeGitHubSignInState(null),
            CopilotCliPath = Path.Combine(AppContext.BaseDirectory, "no-such-copilot"),
            GitExecutable = "webdevloop-no-such-git",
            GhExecutable = "webdevloop-no-such-gh",
            PlaywrightCliExecutable = "webdevloop-no-such-playwright",
            ProbeTimeout = TimeSpan.FromSeconds(5),
        };
        var skills = new BundledSkillsCatalog(new BundledSkillsOptions { Root = Path.Combine(AppContext.BaseDirectory, "no-skills") });
        services.AddPrerequisites(options, "Data Source=:memory:", skills);
        return services.BuildServiceProvider();
    }
}
