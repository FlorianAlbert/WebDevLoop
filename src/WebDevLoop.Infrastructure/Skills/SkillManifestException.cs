namespace WebDevLoop.Infrastructure.Skills;

public sealed class SkillManifestException(IReadOnlyList<string> errors)
    : Exception($"The bundled skill manifest is invalid:{Environment.NewLine}{string.Join(Environment.NewLine, errors)}")
{
    public IReadOnlyList<string> Errors { get; } = errors;
}
