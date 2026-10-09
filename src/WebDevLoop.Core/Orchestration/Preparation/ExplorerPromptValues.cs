using System.Globalization;
using System.Text;
using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Settings;

namespace WebDevLoop.Core.Orchestration.Preparation;

/// <summary>Placeholder values for the explorer prompt (spec context plus the explorer's checkout).</summary>
internal static class ExplorerPromptValues
{
    private const string GitHubBaseUrl = "https://github.com";

    public static IReadOnlyDictionary<string, string> Build(
        SpecRun run,
        RepositoryRecord repository,
        EffectiveSettings settings,
        string skillsRoot,
        RunWorkspaceLayout layout,
        int attempt,
        IReadOnlyList<TicketRun> tickets,
        IReadOnlyList<TicketDependency> dependencies) => new Dictionary<string, string>
        {
            [PromptPlaceholders.RepoOwner] = repository.Owner,
            [PromptPlaceholders.RepoName] = repository.Name,
            [PromptPlaceholders.RepoUrl] = $"{GitHubBaseUrl}/{repository.Owner}/{repository.Name}",
            [PromptPlaceholders.BaseBranch] = (run.BaseBranch ?? settings.BaseBranch).Value,
            [PromptPlaceholders.IntegrationBranch] = run.IntegrationBranch.Value,
            [PromptPlaceholders.IntegrationTipSha] = run.IntegrationTipSha?.Value ?? string.Empty,
            [PromptPlaceholders.WorkspaceRoot] = settings.WorkspaceRootDirectory,
            [PromptPlaceholders.SkillsRoot] = skillsRoot,
            [PromptPlaceholders.RunId] = run.Id.Value,
            [PromptPlaceholders.Attempt] = attempt.ToString(CultureInfo.InvariantCulture),
            [PromptPlaceholders.ParentSpecIssueNumber] = run.ParentIssue.Number.ToString(CultureInfo.InvariantCulture),
            [PromptPlaceholders.ParentSpecTitle] = run.Title,
            [PromptPlaceholders.ParentSpecBody] = run.BodySnapshot,
            [PromptPlaceholders.SpecTickets] = TicketList(tickets, dependencies),
            [PromptPlaceholders.ExplorationNotesPath] = layout.ExplorationNotesDirectory,
            [PromptPlaceholders.WorktreePath] = layout.ExplorerCheckoutDirectory,
        };

    /// <summary>One markdown line per ticket: number, title, issue state, and blocking tickets.</summary>
    /// <remarks>Only open sub-issues are snapshotted as tickets, so every listed ticket is open.</remarks>
    private static string TicketList(IReadOnlyList<TicketRun> tickets, IReadOnlyList<TicketDependency> dependencies)
    {
        Dictionary<TicketRunId, int> numbers = tickets.ToDictionary(ticket => ticket.Id, ticket => ticket.Issue.Number);
        var list = new StringBuilder();
        foreach (TicketRun ticket in tickets.OrderBy(ticket => ticket.Issue.Number))
        {
            int[] blockers = dependencies
                .Where(dependency => dependency.BlockedTicketRunId == ticket.Id && numbers.ContainsKey(dependency.BlockingTicketRunId))
                .Select(dependency => numbers[dependency.BlockingTicketRunId])
                .Order()
                .ToArray();
            string blockedBy = blockers.Length == 0 ? "no blockers" : "blocked by " + string.Join(", ", blockers.Select(number => $"#{number}"));
            list.Append(CultureInfo.InvariantCulture, $"- #{ticket.Issue.Number} {ticket.Title} (open) — {blockedBy}").Append('\n');
        }

        return list.Length == 0 ? "(no open tickets)" : list.ToString().TrimEnd('\n');
    }
}
