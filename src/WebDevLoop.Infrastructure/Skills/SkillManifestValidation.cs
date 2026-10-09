namespace WebDevLoop.Infrastructure.Skills;

public sealed record SkillManifestValidation(IReadOnlyList<string> Errors)
{
    public bool IsValid => Errors.Count == 0;
}
