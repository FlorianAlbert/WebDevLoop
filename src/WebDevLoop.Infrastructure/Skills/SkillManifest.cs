namespace WebDevLoop.Infrastructure.Skills;

/// <summary>Shape of <c>skills-manifest.json</c>: every bundled skill with its provenance and the files that must be shipped.</summary>
internal sealed record SkillManifest(IReadOnlyList<SkillManifestEntry>? Skills);

/// <param name="Path">Skill directory relative to the skills root.</param>
/// <param name="LicenseFile">License text relative to the skills root.</param>
/// <param name="Files">Files relative to the skill directory that must exist, including <c>SKILL.md</c>.</param>
internal sealed record SkillManifestEntry(
    string? Name,
    string? Path,
    string? Origin,
    string? License,
    string? LicenseFile,
    IReadOnlyList<string>? Files);
