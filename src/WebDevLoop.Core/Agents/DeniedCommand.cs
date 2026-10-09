namespace WebDevLoop.Core.Agents;

/// <summary>A shell command an agent must not run, e.g. <c>git push</c> or any <c>gh</c> invocation.</summary>
/// <param name="Subcommand">Null denies every invocation of <paramref name="Executable"/>.</param>
/// <param name="Flag">When set, only invocations carrying this exact flag are denied (e.g. <c>git checkout -b</c>).</param>
public sealed record DeniedCommand(string Executable, string? Subcommand = null, string? Flag = null)
{
    // Global options that consume the following token, e.g. `git -C <path> push`.
    private static readonly HashSet<string> OptionsWithValue = ["-C", "-c", "--git-dir", "--work-tree", "--namespace", "--config-env", "-R", "--repo"];

    /// <param name="words">One simple command, executable first (already stripped of paths, env assignments and wrappers).</param>
    internal bool Matches(IReadOnlyList<string> words)
    {
        if (words.Count == 0 || !string.Equals(words[0], Executable, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (Subcommand is null)
        {
            return true;
        }

        int index = 1;
        while (index < words.Count && words[index].StartsWith('-'))
        {
            index += OptionsWithValue.Contains(words[index]) ? 2 : 1;
        }

        if (index >= words.Count || !string.Equals(words[index], Subcommand, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return Flag is null || words.Skip(index + 1).Contains(Flag, StringComparer.Ordinal);
    }

    public override string ToString() => string.Join(' ', new[] { Executable, Subcommand ?? "*", Flag }.Where(part => part is not null));
}
