namespace WebDevLoop.Infrastructure.Skills;

/// <param name="Directory">Absolute directory of the copied skill (contains its <c>SKILL.md</c>).</param>
public sealed record BundledSkill(string Name, string Directory, string Origin, string License);
