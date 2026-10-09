using System.Text.Json;

namespace WebDevLoop.Infrastructure.Skills;

/// <summary>
/// Resolves the agent skills copied into the app output from the manifest that lists each skill's origin, license and
/// expected files. Startup validation uses <see cref="Validate"/>; agent sessions use the resolved root.
/// </summary>
public sealed class BundledSkillsCatalog(BundledSkillsOptions options)
{
    public const string ManifestFileName = "skills-manifest.json";

    private const string SkillDefinitionFile = "SKILL.md";

    private static readonly JsonSerializerOptions ManifestJson = new(JsonSerializerDefaults.Web);

    private static readonly StringComparison PathComparison =
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    public SkillManifestValidation Validate() => new(Load().Errors);

    /// <exception cref="SkillManifestException">The manifest is missing or invalid, or an expected file was not copied.</exception>
    public BundledSkills Resolve()
    {
        (IReadOnlyList<BundledSkill> skills, IReadOnlyList<string> errors) = Load();
        return errors.Count > 0 ? throw new SkillManifestException(errors) : new BundledSkills(Root, skills);
    }

    private string Root => Path.TrimEndingDirectorySeparator(Path.GetFullPath(options.Root));

    private (IReadOnlyList<BundledSkill> Skills, IReadOnlyList<string> Errors) Load()
    {
        string manifestPath = Path.Combine(Root, ManifestFileName);
        if (!File.Exists(manifestPath))
        {
            return ([], [$"Skill manifest '{ManifestFileName}' was not found in '{Root}'."]);
        }

        SkillManifest? manifest;
        try
        {
            manifest = JsonSerializer.Deserialize<SkillManifest>(File.ReadAllText(manifestPath), ManifestJson);
        }
        catch (JsonException exception)
        {
            return ([], [$"Skill manifest '{manifestPath}' is not valid JSON: {exception.Message}"]);
        }

        if (manifest?.Skills is not { Count: > 0 } entries)
        {
            return ([], [$"Skill manifest '{manifestPath}' lists no skills."]);
        }

        var skills = new List<BundledSkill>();
        var errors = new List<string>();
        foreach (SkillManifestEntry entry in entries)
        {
            List<string> entryErrors = ValidateEntry(entry, out string? directory);
            if (entryErrors.Count == 0)
            {
                skills.Add(new BundledSkill(entry.Name!, directory!, entry.Origin!, entry.License!));
            }

            errors.AddRange(entryErrors);
        }

        return (skills, errors);
    }

    private List<string> ValidateEntry(SkillManifestEntry entry, out string? directory)
    {
        string name = string.IsNullOrWhiteSpace(entry.Name) ? "(unnamed)" : entry.Name;
        var errors = new List<string>();
        void Fail(string problem) => errors.Add($"Skill '{name}': {problem}");

        if (string.IsNullOrWhiteSpace(entry.Name))
        {
            Fail("name is required.");
        }

        if (string.IsNullOrWhiteSpace(entry.Origin))
        {
            Fail("origin URL is required.");
        }

        if (string.IsNullOrWhiteSpace(entry.License))
        {
            Fail("license is required.");
        }

        directory = ResolveInsideRoot(entry.Path, "path", Fail);
        string? licenseFile = ResolveInsideRoot(entry.LicenseFile, "licenseFile", Fail);
        if (licenseFile is not null && !File.Exists(licenseFile))
        {
            Fail($"license file '{entry.LicenseFile}' is missing.");
        }

        IReadOnlyList<string> files = entry.Files ?? [];
        if (!files.Contains(SkillDefinitionFile, StringComparer.Ordinal))
        {
            Fail($"expected files must include {SkillDefinitionFile}.");
        }

        if (directory is not null)
        {
            string skillDirectory = directory;
            string[] missing = files
                .Where(file => ResolveInside(skillDirectory, file) is not { } path || !File.Exists(path))
                .ToArray();
            if (missing.Length > 0)
            {
                Fail($"expected files missing from '{skillDirectory}': {string.Join(", ", missing)}.");
            }
        }

        return errors;
    }

    private string? ResolveInsideRoot(string? relativePath, string field, Action<string> fail)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            fail($"{field} is required.");
            return null;
        }

        string? resolved = ResolveInside(Root, relativePath);
        if (resolved is null)
        {
            fail($"{field} '{relativePath}' must stay inside the skills root.");
        }

        return resolved;
    }

    private static string? ResolveInside(string root, string relativePath)
    {
        if (Path.IsPathRooted(relativePath))
        {
            return null;
        }

        string resolved = Path.GetFullPath(Path.Combine(root, relativePath));
        return resolved.StartsWith(root + Path.DirectorySeparatorChar, PathComparison) ? resolved : null;
    }
}
