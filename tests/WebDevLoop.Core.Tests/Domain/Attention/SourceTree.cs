namespace WebDevLoop.Core.Tests.Domain.Attention;

/// <summary>Reads the repository's source files for tests that guard how the code is written.</summary>
internal static class SourceTree
{
    private static readonly Lazy<string> Root = new(FindRoot);

    public static string File(string relativePath) => Path.Combine(Root.Value, relativePath);

    public static IEnumerable<(string Path, string Text)> Sources(string directory) =>
        Directory.EnumerateFiles(File(directory), "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                && !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Select(path => (path.Replace(Path.DirectorySeparatorChar, '/'), System.IO.File.ReadAllText(path)));

    /// <summary>The argument lists of calls to the named methods (definitions with a return type in front are skipped).</summary>
    public static IEnumerable<string> CallsOf(string text, params string[] methods)
    {
        foreach (string method in methods)
        {
            int index = 0;
            while ((index = text.IndexOf(method + "(", index, StringComparison.Ordinal)) >= 0)
            {
                int start = index + method.Length + 1;
                bool isDefinition = index > 0 && (char.IsLetterOrDigit(text[index - 1]) || text[index - 1] == '>' && IsTypeBefore(text, index));
                index = start;
                if (isDefinition || IsDeclaration(text, start))
                {
                    continue;
                }

                int depth = 1;
                int end = start;
                while (end < text.Length && depth > 0)
                {
                    depth += text[end] == '(' ? 1 : text[end] == ')' ? -1 : 0;
                    end++;
                }

                yield return text[start..(end - 1)];
            }
        }
    }

    private static bool IsTypeBefore(string text, int index) => text[..index].TrimEnd().EndsWith('>');

    /// <summary>A declaration's parameter list starts with a type and a name, e.g. <c>(TicketRun ticket, AttentionReason reason</c>.</summary>
    private static bool IsDeclaration(string text, int start)
    {
        int end = text.IndexOfAny([',', ')'], start);
        string firstParameter = text[start..end].Trim();
        return System.Text.RegularExpressions.Regex.IsMatch(firstParameter, @"^[A-Z][A-Za-z<>?\[\]]*\s+[a-z][A-Za-z]*$");
    }

    private static string FindRoot()
    {
        for (string? directory = AppContext.BaseDirectory; directory is not null; directory = Path.GetDirectoryName(directory))
        {
            if (System.IO.File.Exists(Path.Combine(directory, "WebDevLoop.slnx")))
            {
                return directory;
            }
        }

        throw new InvalidOperationException("The repository root (WebDevLoop.slnx) was not found above the test binaries.");
    }
}
