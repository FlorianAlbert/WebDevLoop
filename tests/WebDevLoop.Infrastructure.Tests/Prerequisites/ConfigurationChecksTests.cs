using System.Text.Json;
using WebDevLoop.Core.Ports;
using WebDevLoop.Infrastructure.GitHub.Auth;
using WebDevLoop.Infrastructure.Prerequisites;
using WebDevLoop.Infrastructure.Skills;

namespace WebDevLoop.Infrastructure.Tests.Prerequisites;

public sealed class ConfigurationChecksTests : IDisposable
{
    private readonly TestDirectory _skillsDirectory = new("prereq-skills");

    public void Dispose() => _skillsDirectory.Dispose();

    [Fact]
    public async Task complete_bundled_skills_pass()
    {
        WriteSkillBundle(("tdd", ["SKILL.md"]), ("code-review", ["SKILL.md"]));
        WriteSkillFile("tdd/SKILL.md");
        WriteSkillFile("code-review/SKILL.md");

        PrerequisiteCheck result = await SkillsCheck().RunAsync(CancellationToken.None);

        Assert.Equal(PrerequisiteStatus.Passed, result.Status);
    }

    [Fact]
    public async Task missing_required_skill_fails_readiness_with_the_skill_name()
    {
        WriteSkillBundle(("tdd", ["SKILL.md"]), ("code-review", ["SKILL.md"]));
        WriteSkillFile("tdd/SKILL.md");

        PrerequisiteCheck result = await SkillsCheck().RunAsync(CancellationToken.None);
        PrerequisiteReport report = await new PrerequisiteValidator([SkillsCheck()]).ValidateAsync(CancellationToken.None);

        Assert.Equal(PrerequisiteStatus.Failed, result.Status);
        Assert.Contains("code-review", result.Message);
        Assert.DoesNotContain("tdd", result.Message);
        Assert.False(report.IsReady);
    }

    [Fact]
    public async Task missing_skill_manifest_fails()
    {
        PrerequisiteCheck result = await SkillsCheck().RunAsync(CancellationToken.None);

        Assert.Equal(PrerequisiteStatus.Failed, result.Status);
        Assert.Contains(BundledSkillsCatalog.ManifestFileName, result.Message);
    }

    [Fact]
    public async Task loadable_libgit2sharp_passes_with_its_version()
    {
        PrerequisiteCheck result = await new LibGit2SharpCheck(new FakeLibGit2Probe(() => "0.32.0")).RunAsync(CancellationToken.None);

        Assert.Equal(PrerequisiteStatus.Passed, result.Status);
        Assert.Contains("0.32.0", result.Message);
    }

    [Fact]
    public async Task libgit2sharp_native_load_failure_fails_with_the_reason()
    {
        var probe = new FakeLibGit2Probe(() => throw new DllNotFoundException("libgit2-abc.so"));

        PrerequisiteCheck result = await new LibGit2SharpCheck(probe).RunAsync(CancellationToken.None);

        Assert.Equal(PrerequisiteStatus.Failed, result.Status);
        Assert.Contains("libgit2-abc.so", result.Message);
    }

    [Fact]
    public async Task configured_and_signed_in_passes_naming_the_user()
    {
        PrerequisiteCheck result = await AuthCheck(Configured, "octocat");

        Assert.Equal(PrerequisiteStatus.Passed, result.Status);
        Assert.Contains("octocat", result.Message);
    }

    [Fact]
    public async Task configured_but_signed_out_fails_with_a_sign_in_remediation()
    {
        PrerequisiteCheck result = await AuthCheck(Configured, login: null);

        Assert.Equal(PrerequisiteStatus.Failed, result.Status);
        Assert.Contains("Sign in with GitHub", result.Remediation);
    }

    [Theory]
    [InlineData("Iv23.abc", null, "client secret")]
    [InlineData(null, "secret", "client id")]
    public async Task half_configured_github_app_fails_naming_the_missing_setting(string? clientId, string? secret, string missing)
    {
        PrerequisiteCheck result = await AuthCheck(new GitHubAuthOptions { AppClientId = clientId, AppClientSecret = secret }, "octocat");

        Assert.Equal(PrerequisiteStatus.Failed, result.Status);
        Assert.Contains(missing, result.Message);
    }

    [Fact]
    public async Task unconfigured_sign_in_fails_naming_both_settings()
    {
        PrerequisiteCheck result = await AuthCheck(new GitHubAuthOptions(), login: null);

        Assert.Equal(PrerequisiteStatus.Failed, result.Status);
        Assert.Contains("AppClientId", result.Message);
        Assert.Contains("AppClientSecret", result.Message);
        Assert.False(string.IsNullOrWhiteSpace(result.Remediation));
    }

    private static GitHubAuthOptions Configured => new() { AppClientId = "Iv23.abc", AppClientSecret = "very-secret-value" };

    private static Task<PrerequisiteCheck> AuthCheck(GitHubAuthOptions auth, string? login) =>
        new GitHubAuthCheck(TestPrerequisiteOptions.Create(gitHubAuth: auth, gitHubSignIn: new FakeGitHubSignInState(login))).RunAsync(CancellationToken.None);

    private BundledSkillsCheck SkillsCheck() =>
        new(new BundledSkillsCatalog(new BundledSkillsOptions { Root = _skillsDirectory.Path }));

    private void WriteSkillBundle(params (string Name, string[] Files)[] skills)
    {
        var entries = skills.Select(skill => new
        {
            name = skill.Name,
            path = skill.Name,
            origin = $"https://example.com/{skill.Name}",
            license = "MIT",
            licenseFile = "LICENSES/MIT.txt",
            files = skill.Files,
        });
        WriteSkillFile("LICENSES/MIT.txt");
        File.WriteAllText(
            Path.Combine(_skillsDirectory.Path, BundledSkillsCatalog.ManifestFileName),
            JsonSerializer.Serialize(new { skills = entries }));
    }

    private void WriteSkillFile(string relativePath)
    {
        string path = Path.Combine(_skillsDirectory.Path, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "content");
    }
}
