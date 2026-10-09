namespace WebDevLoop.Core.Orchestration.Preparation;

/// <param name="SkillsRoot">Absolute directory of the bundled agent skills, rendered as <c>{skills_root}</c>.</param>
/// <param name="ExplorationEnabled">Whether preparation runs the optional explorer agent (workflow step 2).</param>
public sealed record SpecPreparationOptions(string SkillsRoot, bool ExplorationEnabled = true);
