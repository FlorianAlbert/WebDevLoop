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
    public async Task configured_github_app_passes()
    {
        PrerequisiteCheck result = await AuthCheck(new GitHubAuthOptions { AppClientId = "Iv1.abc", AppPrivateKeyPem = "pem" });

        Assert.Equal(PrerequisiteStatus.Passed, result.Status);
    }

    [Theory]
    [InlineData("Iv1.abc", null, "private key")]
    [InlineData(null, "pem", "client id")]
    public async Task half_configured_github_app_fails_naming_the_missing_setting(string? clientId, string? pem, string missing)
    {
        PrerequisiteCheck result = await AuthCheck(new GitHubAuthOptions { AppClientId = clientId, AppPrivateKeyPem = pem });

        Assert.Equal(PrerequisiteStatus.Failed, result.Status);
        Assert.Contains(missing, result.Message);
    }

    [Fact]
    public async Task no_github_credentials_fail()
    {
        PrerequisiteCheck result = await AuthCheck(new GitHubAuthOptions());

        Assert.Equal(PrerequisiteStatus.Failed, result.Status);
        Assert.False(string.IsNullOrWhiteSpace(result.Remediation));
    }

    [Fact]
    public async Task pat_only_with_fallback_enabled_is_a_warning()
    {
        PrerequisiteCheck result = await AuthCheck(new GitHubAuthOptions { PatFallbackEnabled = true, UserToken = "ghp_secret" });

        Assert.Equal(PrerequisiteStatus.Warning, result.Status);
        Assert.DoesNotContain("ghp_secret", result.Message);
    }

    [Theory]
    [InlineData(true, null)]
    [InlineData(false, "ghp_secret")]
    public async Task pat_without_usable_fallback_fails(bool fallbackEnabled, string? token)
    {
        PrerequisiteCheck result = await AuthCheck(new GitHubAuthOptions { PatFallbackEnabled = fallbackEnabled, UserToken = token });

        Assert.Equal(PrerequisiteStatus.Failed, result.Status);
    }

    [Fact]
    public async Task github_app_with_pat_fallback_enabled_but_no_token_warns()
    {
        PrerequisiteCheck result = await AuthCheck(
            new GitHubAuthOptions { AppClientId = "Iv1.abc", AppPrivateKeyPem = "pem", PatFallbackEnabled = true });

        Assert.Equal(PrerequisiteStatus.Warning, result.Status);
    }

    private static Task<PrerequisiteCheck> AuthCheck(GitHubAuthOptions auth) =>
        new GitHubAuthCheck(TestPrerequisiteOptions.Create(gitHubAuth: auth)).RunAsync(CancellationToken.None);

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
