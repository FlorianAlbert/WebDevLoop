namespace WebDevLoop.Infrastructure.Skills;

/// <param name="Root">Skill root passed to Copilot sessions as skill directory and rendered as <c>{skills_root}</c>.</param>
public sealed record BundledSkills(string Root, IReadOnlyList<BundledSkill> Skills);
