using WebDevLoop.Core.Domain;

namespace WebDevLoop.Core.Orchestration.Results;

/// <summary>Validation shared by report contracts; failures surface as <see cref="InvalidAgentReportException"/>.</summary>
internal static class ReportGuard
{
    private const int MaxHeadlineLength = 100;
    private static readonly StringComparer FindingIds = StringComparer.OrdinalIgnoreCase;

    /// <summary>
    /// A short issue title: the reporter's <paramref name="title"/> when given, otherwise the first sentence of
    /// <paramref name="fallbackText"/>. Anything longer than the limit is cut at a word boundary; the full text belongs in the body.
    /// </summary>
    public static string Headline(string? title, string fallbackText)
    {
        bool explicitTitle = !string.IsNullOrWhiteSpace(title);
        string line = (explicitTitle ? title! : fallbackText).Trim().Split('\n', 2)[0].Trim();
        if (!explicitTitle)
        {
            line = FirstSentence(line);
        }

        return line.Length <= MaxHeadlineLength ? line : CutAtWord(line);
    }

    private static string FirstSentence(string line)
    {
        for (int index = 0; index < line.Length - 1; index++)
        {
            if (line[index] is '.' or '!' or '?' && char.IsWhiteSpace(line[index + 1]) && index + 1 <= MaxHeadlineLength)
            {
                return line[..(index + 1)];
            }
        }

        return line;
    }

    private static string CutAtWord(string line)
    {
        string head = line[..(MaxHeadlineLength - 1)];
        int boundary = line[MaxHeadlineLength - 1] is ' ' or '\t' ? head.Length : head.LastIndexOfAny([' ', '\t']);
        string cut = boundary > MaxHeadlineLength / 2 ? head[..boundary] : head;
        return cut.TrimEnd(' ', '\t', '.', ':', ';', ',', '-', '—') + "…";
    }

    public static string RequireText(string? value, string field) =>
        string.IsNullOrWhiteSpace(value) ? throw new InvalidAgentReportException($"'{field}' must not be blank.") : value;

    public static string? OptionalText(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;

    public static CommitSha RequireCommit(CommitSha? commit, string field) =>
        commit is { Value: not null } sha ? sha : throw new InvalidAgentReportException($"'{field}' must be a commit SHA.");

    public static int? OptionalLine(int? line, string field) =>
        line is < 1 ? throw new InvalidAgentReportException($"'{field}' must be a 1-based line number.") : line;

    public static IReadOnlyList<T> RequireList<T>(IReadOnlyList<T>? items, string field)
        where T : class =>
        items is null || items.Any(item => item is null)
            ? throw new InvalidAgentReportException($"'{field}' must be a list without null entries.")
            : items;

    public static IReadOnlyList<string> RequireTextList(IReadOnlyList<string>? items, string field, bool requireAny = false)
    {
        if (items is null || items.Any(string.IsNullOrWhiteSpace))
        {
            throw new InvalidAgentReportException($"'{field}' must be a list without blank entries.");
        }

        return requireAny && items.Count == 0 ? throw new InvalidAgentReportException($"'{field}' must not be empty.") : items;
    }

    /// <summary>Trimmed dependency ids; a finding cannot be blocked by itself.</summary>
    public static IReadOnlyList<string> RequireDependencies(string? ownId, IReadOnlyList<string>? blockedBy)
    {
        IReadOnlyList<string> ids = RequireTextList(blockedBy ?? [], "blocked_by");
        string[] trimmed = [.. ids.Select(id => id.Trim()).Distinct(FindingIds)];
        return ownId is not null && trimmed.Contains(ownId, FindingIds)
            ? throw new InvalidAgentReportException($"Finding '{ownId}' cannot be blocked by itself.")
            : trimmed;
    }

    /// <summary>Findings must be present exactly when the verdict says so.</summary>
    public static IReadOnlyList<T> RequireFindings<T>(IReadOnlyList<T>? findings, bool expected, string field, Enum verdict)
        where T : Finding
    {
        IReadOnlyList<T> list = RequireList(findings, field);
        return expected switch
        {
            true when list.Count == 0 => throw new InvalidAgentReportException($"Verdict '{verdict}' requires at least one entry in '{field}'."),
            false when list.Count > 0 => throw new InvalidAgentReportException($"Verdict '{verdict}' must not carry '{field}'."),
            _ => RequireConsistentDependencies(list, field),
        };
    }

    /// <summary>Finding ids are unique within a report and <c>blocked_by</c> may only name ids of the same report.</summary>
    private static IReadOnlyList<T> RequireConsistentDependencies<T>(IReadOnlyList<T> findings, string field)
        where T : Finding
    {
        string[] ids = [.. findings.Select(finding => finding.Id).OfType<string>()];
        if (ids.Distinct(FindingIds).Count() != ids.Length)
        {
            throw new InvalidAgentReportException($"'{field}' contains the same finding id more than once.");
        }

        if (findings.SelectMany(finding => finding.BlockedBy).FirstOrDefault(id => !ids.Contains(id, FindingIds)) is { } unknown)
        {
            throw new InvalidAgentReportException($"'blocked_by' names finding id '{unknown}', which is not an id in '{field}'.");
        }

        return findings;
    }
}
