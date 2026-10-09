using System.Text;
using System.Text.RegularExpressions;

namespace WebDevLoop.Core.Agents;

/// <summary>
/// Splits a shell command line into simple commands (operators, command substitutions, and <c>sh -c</c> scripts included)
/// so denied commands are found wherever they appear, while quoted arguments such as commit messages are left alone.
/// This is defense in depth: agents also run without GitHub write credentials.
/// </summary>
internal static partial class ShellCommandGuard
{
    private const int MaxNestingDepth = 8;

    private static readonly HashSet<string> Wrappers = ["sudo", "env", "command", "exec", "nohup", "time", "xargs", "nice", "timeout"];
    private static readonly HashSet<string> Shells = ["sh", "bash", "zsh", "dash", "ksh"];

    public static DeniedCommand? FindDenied(string commandLine, IReadOnlyList<DeniedCommand> denied) =>
        string.IsNullOrWhiteSpace(commandLine)
            ? null
            : SimpleCommands(commandLine, depth: 0)
                .Select(words => denied.FirstOrDefault(rule => rule.Matches(words)))
                .FirstOrDefault(rule => rule is not null);

    private static IEnumerable<IReadOnlyList<string>> SimpleCommands(string commandLine, int depth)
    {
        if (depth > MaxNestingDepth)
        {
            yield break;
        }

        (List<List<string>> commands, List<string> substitutions) = Split(commandLine);
        foreach (List<string> raw in commands)
        {
            List<string> words = Unwrap(raw);
            if (words.Count == 0)
            {
                continue;
            }

            yield return words;

            if (Shells.Contains(words[0]) && ScriptArgument(words) is { } script)
            {
                foreach (IReadOnlyList<string> nested in SimpleCommands(script, depth + 1))
                {
                    yield return nested;
                }
            }
        }

        foreach (string substitution in substitutions)
        {
            foreach (IReadOnlyList<string> nested in SimpleCommands(substitution, depth + 1))
            {
                yield return nested;
            }
        }
    }

    private static (List<List<string>> Commands, List<string> Substitutions) Split(string commandLine)
    {
        var commands = new List<List<string>>();
        var substitutions = new List<string>();
        var current = new List<string>();
        var word = new StringBuilder();
        bool inWord = false;
        char quote = '\0';

        void EndWord()
        {
            if (inWord)
            {
                current.Add(word.ToString());
                word.Clear();
                inWord = false;
            }
        }

        void EndCommand()
        {
            EndWord();
            if (current.Count > 0)
            {
                commands.Add(current);
                current = [];
            }
        }

        for (int i = 0; i < commandLine.Length; i++)
        {
            char c = commandLine[i];

            if (quote == '\'')
            {
                if (c == '\'')
                {
                    quote = '\0';
                }
                else
                {
                    word.Append(c);
                }

                continue;
            }

            if (c == '\\' && i + 1 < commandLine.Length)
            {
                word.Append(commandLine[++i]);
                inWord = true;
                continue;
            }

            if (c == '$' && i + 1 < commandLine.Length && commandLine[i + 1] == '(')
            {
                int end = MatchingParenthesis(commandLine, i + 1);
                substitutions.Add(commandLine[(i + 2)..end]);
                i = end;
                inWord = true;
                continue;
            }

            if (c == '`')
            {
                int end = commandLine.IndexOf('`', i + 1);
                end = end < 0 ? commandLine.Length : end;
                substitutions.Add(commandLine[(i + 1)..end]);
                i = end;
                inWord = true;
                continue;
            }

            if (quote == '"')
            {
                if (c == '"')
                {
                    quote = '\0';
                }
                else
                {
                    word.Append(c);
                }

                continue;
            }

            switch (c)
            {
                case '\'' or '"':
                    quote = c;
                    inWord = true;
                    break;
                case ';' or '&' or '|' or '\n' or '(' or ')' or '{' or '}':
                    EndCommand();
                    break;
                case var _ when char.IsWhiteSpace(c):
                    EndWord();
                    break;
                default:
                    word.Append(c);
                    inWord = true;
                    break;
            }
        }

        EndCommand();
        return (commands, substitutions);
    }

    private static int MatchingParenthesis(string text, int open)
    {
        int depth = 0;
        for (int i = open; i < text.Length; i++)
        {
            depth += text[i] switch { '(' => 1, ')' => -1, _ => 0 };
            if (depth == 0)
            {
                return i;
            }
        }

        return text.Length;
    }

    /// <summary>Drops env assignments and wrapper commands (with their options) and reduces the executable to its file name.</summary>
    private static List<string> Unwrap(List<string> words)
    {
        int index = 0;
        while (index < words.Count)
        {
            string name = Path.GetFileName(words[index]);
            if (Assignment().IsMatch(words[index]))
            {
                index++;
            }
            else if (Wrappers.Contains(name))
            {
                index++;
                while (index < words.Count && (words[index].StartsWith('-') || Assignment().IsMatch(words[index])))
                {
                    index++;
                }
            }
            else
            {
                break;
            }
        }

        List<string> remaining = words.Skip(index).ToList();
        if (remaining.Count > 0)
        {
            remaining[0] = Path.GetFileName(remaining[0]).ToLowerInvariant();
        }

        return remaining;
    }

    private static string? ScriptArgument(IReadOnlyList<string> words)
    {
        for (int i = 1; i < words.Count - 1; i++)
        {
            if (words[i].StartsWith('-') && !words[i].StartsWith("--", StringComparison.Ordinal) && words[i].Contains('c'))
            {
                return words[i + 1];
            }
        }

        return null;
    }

    [GeneratedRegex("^[A-Za-z_][A-Za-z0-9_]*=")]
    private static partial Regex Assignment();
}
