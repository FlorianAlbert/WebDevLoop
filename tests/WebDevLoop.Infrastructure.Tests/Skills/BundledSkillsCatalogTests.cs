using System.Text.Json;
using WebDevLoop.Infrastructure.Skills;

namespace WebDevLoop.Infrastructure.Tests.Skills;

public sealed class BundledSkillsCatalogTests : IDisposable
{
    private readonly TestDirectory _directory = new("skills");

    private string Root => _directory.Path;

    public void Dispose() => _directory.Dispose();

    [Fact]
    public void validation_fails_when_an_expected_skill_file_is_missing()
    {
        WriteManifest(Entry("tdd", files: ["SKILL.md", "tests.md"]));
        WriteFile("tdd/SKILL.md");
        WriteFile("LICENSES/MIT.txt");

        SkillManifestValidation validation = Catalog().Validate();

        Assert.False(validation.IsValid);
        string error = Assert.Single(validation.Errors);
        Assert.Contains("tdd", error);
        Assert.Contains("tests.md", error);
        SkillManifestException exception = Assert.Throws<SkillManifestException>(() => Catalog().Resolve());
        Assert.Contains("tests.md", exception.Message);
    }

    [Fact]
    public void resolves_copied_skill_directories_with_origin_and_license()
    {
        WriteManifest(Entry("tdd", files: ["SKILL.md", "tests.md"]), Entry("code-review", files: ["SKILL.md"]));
        WriteFile("tdd/SKILL.md");
        WriteFile("tdd/tests.md");
        WriteFile("code-review/SKILL.md");
        WriteFile("LICENSES/MIT.txt");

        BundledSkills skills = Catalog().Resolve();

        Assert.Equal(Root, skills.Root);
        Assert.Equal(["tdd", "code-review"], skills.Skills.Select(skill => skill.Name));
        BundledSkill tdd = skills.Skills[0];
        Assert.Equal(Path.Combine(Root, "tdd"), tdd.Directory);
        Assert.Equal("https://github.com/example/skills/tdd", tdd.Origin);
        Assert.Equal("MIT", tdd.License);
    }

    [Fact]
    public void validation_fails_without_a_manifest()
    {
        SkillManifestValidation validation = Catalog().Validate();

        Assert.Contains(validation.Errors, error => error.Contains(BundledSkillsCatalog.ManifestFileName));
    }

    [Fact]
    public void validation_requires_origin_license_and_skill_md_for_every_entry()
    {
        WriteManifest(new { name = "tdd", path = "tdd", origin = "", license = " ", licenseFile = "LICENSES/MIT.txt", files = new[] { "tests.md" } });
        WriteFile("tdd/tests.md");
        WriteFile("LICENSES/MIT.txt");

        SkillManifestValidation validation = Catalog().Validate();

        Assert.Contains(validation.Errors, error => error.Contains("origin"));
        Assert.Contains(validation.Errors, error => error.Contains("license"));
        Assert.Contains(validation.Errors, error => error.Contains("SKILL.md"));
    }

    [Fact]
    public void validation_fails_when_the_license_file_is_missing()
    {
        WriteManifest(Entry("tdd", files: ["SKILL.md"]));
        WriteFile("tdd/SKILL.md");

        SkillManifestValidation validation = Catalog().Validate();

        Assert.Contains(validation.Errors, error => error.Contains("LICENSES/MIT.txt"));
    }

    [Fact]
    public void validation_rejects_paths_escaping_the_skills_root()
    {
        WriteManifest(new { name = "tdd", path = "../outside", origin = "https://example.com", license = "MIT", licenseFile = "../LICENSE", files = new[] { "SKILL.md" } });

        SkillManifestValidation validation = Catalog().Validate();

        Assert.Contains(validation.Errors, error => error.Contains("../outside"));
        Assert.Contains(validation.Errors, error => error.Contains("../LICENSE"));
    }

    [Fact]
    public void the_shipped_skill_bundle_is_copied_to_the_output_and_valid()
    {
        var catalog = new BundledSkillsCatalog(new BundledSkillsOptions());

        SkillManifestValidation validation = catalog.Validate();

        Assert.True(validation.IsValid, string.Join(Environment.NewLine, validation.Errors));
        BundledSkills skills = catalog.Resolve();
        Assert.Equal(Path.Combine(AppContext.BaseDirectory, BundledSkillsOptions.DefaultDirectoryName), skills.Root);
        Assert.Superset(
            new HashSet<string> { "tdd", "code-review", "codebase-design", "implement-spec", "to-spec", "to-tickets", "setup-matt-pocock-skills", "triage", "playwright-cli" },
            skills.Skills.Select(skill => skill.Name).ToHashSet());
        Assert.All(skills.Skills, skill => Assert.True(File.Exists(Path.Combine(skill.Directory, "SKILL.md"))));
    }

    private BundledSkillsCatalog Catalog() => new(new BundledSkillsOptions { Root = Root });

    private static object Entry(string name, string[] files) => new
    {
        name,
        path = name,
        origin = $"https://github.com/example/skills/{name}",
        license = "MIT",
        licenseFile = "LICENSES/MIT.txt",
        files,
    };

    private void WriteManifest(params object[] skills) =>
        File.WriteAllText(Path.Combine(Root, BundledSkillsCatalog.ManifestFileName), JsonSerializer.Serialize(new { skills }));

    private void WriteFile(string relativePath)
    {
        string path = Path.Combine(Root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "content");
    }
}
