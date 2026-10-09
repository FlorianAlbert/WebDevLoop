using WebDevLoop.Core.Domain;

namespace WebDevLoop.Core.Orchestration.Completion.ReadyAndMerge;

internal static class PullRequestNumbers
{
    public static string Describe(IEnumerable<PullRequestNumber> numbers) => string.Join(", ", numbers.Select(number => $"#{number}"));
}
