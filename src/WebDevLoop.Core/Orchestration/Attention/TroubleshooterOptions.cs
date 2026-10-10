namespace WebDevLoop.Core.Orchestration.Attention;

/// <param name="SkillsRoot">Directory holding the copied bundled skills (<c>{skills_root}</c>).</param>
/// <param name="LogTailEntries">How many of the newest log entries of the last agent step the prompt carries.</param>
public sealed record TroubleshooterOptions(string SkillsRoot, int LogTailEntries = 40);
