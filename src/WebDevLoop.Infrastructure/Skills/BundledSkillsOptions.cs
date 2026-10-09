namespace WebDevLoop.Infrastructure.Skills;

public sealed class BundledSkillsOptions
{
    public const string DefaultDirectoryName = "skills";

    /// <summary>Directory holding the copied skills and their manifest; defaults to <c>skills/</c> in the app output.</summary>
    public string Root { get; init; } = Path.Combine(AppContext.BaseDirectory, DefaultDirectoryName);
}
