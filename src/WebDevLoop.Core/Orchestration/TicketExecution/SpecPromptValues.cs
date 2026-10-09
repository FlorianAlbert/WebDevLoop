using System.Globalization;
using System.Text;
using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Orchestration.Preparation;
using WebDevLoop.Core.Settings;

namespace WebDevLoop.Core.Orchestration.TicketExecution;

/// <summary>Placeholder values shared by every agent working on a running spec (implementer, fixer, reviewers).</summary>
internal static class SpecPromptValues
{
    private const string GitHubBaseUrl = "https://github.com";

    /// <summary>Repository, run, integration branch, and parent-spec context.</summary>
    public static Dictionary<string, string> ForSpec(
        SpecRun spec,
        RepositoryRecord repository,
        EffectiveSettings settings,
        RunWorkspaceLayout layout,
        int attempt,
        CommitSha integrationTip,
        string skillsRoot,
        IReadOnlyList<TicketRun> tickets,
        IReadOnlyList<TicketDependency> dependencies) => new()
        {
            [PromptPlaceholders.RepoOwner] = repository.Owner,
            [PromptPlaceholders.RepoName] = repository.Name,
            [PromptPlaceholders.RepoUrl] = $"{GitHubBaseUrl}/{repository.Owner}/{repository.Name}",
            [PromptPlaceholders.BaseBranch] = (spec.BaseBranch ?? settings.BaseBranch).Value,
            [PromptPlaceholders.IntegrationBranch] = spec.IntegrationBranch.Value,
            [PromptPlaceholders.IntegrationTipSha] = integrationTip.Value,
            [PromptPlaceholders.WorkspaceRoot] = settings.WorkspaceRootDirectory,
            [PromptPlaceholders.SkillsRoot] = skillsRoot,
            [PromptPlaceholders.RunId] = spec.Id.Value,
            [PromptPlaceholders.Attempt] = attempt.ToString(CultureInfo.InvariantCulture),
            [PromptPlaceholders.ParentSpecIssueNumber] = spec.ParentIssue.Number.ToString(CultureInfo.InvariantCulture),
            [PromptPlaceholders.ParentSpecTitle] = spec.Title,
            [PromptPlaceholders.ParentSpecBody] = spec.BodySnapshot,
            [PromptPlaceholders.SpecTickets] = TicketList(tickets, dependencies),
            [PromptPlaceholders.ExplorationNotesPath] = layout.ExplorationNotesDirectory,
        };

    /// <summary>Adds the ticket's issue snapshot and its blocking tickets.</summary>
    public static void AddTicket(
        Dictionary<string, string> values,
        TicketRun ticket,
        IReadOnlyList<TicketRun> tickets,
        IReadOnlyList<TicketDependency> dependencies)
    {
        values[PromptPlaceholders.TicketIssueNumber] = ticket.Issue.Number.ToString(CultureInfo.InvariantCulture);
        values[PromptPlaceholders.TicketTitle] = ticket.Title;
        values[PromptPlaceholders.TicketBody] = ticket.BodySnapshot;
        values[PromptPlaceholders.TicketDependencies] = Blockers(ticket, tickets, dependencies);
    }

    /// <summary>One markdown line per ticket of the spec: number, title, status, and blocking tickets.</summary>
    private static string TicketList(IReadOnlyList<TicketRun> tickets, IReadOnlyList<TicketDependency> dependencies)
    {
        var list = new StringBuilder();
        foreach (TicketRun ticket in tickets.OrderBy(ticket => ticket.Issue.Number))
        {
            int[] blockers = BlockersOf(ticket, tickets, dependencies).Select(blocker => blocker.Issue.Number).ToArray();
            string blockedBy = blockers.Length == 0 ? "no blockers" : "blocked by " + string.Join(", ", blockers.Select(number => $"#{number}"));
            list.Append(CultureInfo.InvariantCulture, $"- #{ticket.Issue.Number} {ticket.Title} ({ticket.Status}) — {blockedBy}").Append('\n');
        }

        return list.Length == 0 ? "(no tickets)" : list.ToString().TrimEnd('\n');
    }

    private static string Blockers(TicketRun ticket, IReadOnlyList<TicketRun> tickets, IReadOnlyList<TicketDependency> dependencies)
    {
        string[] lines = BlockersOf(ticket, tickets, dependencies)
            .Select(blocker => $"- #{blocker.Issue.Number} {blocker.Title} ({blocker.Status})")
            .ToArray();
        return lines.Length == 0 ? "(no blockers)" : string.Join('\n', lines);
    }

    private static IEnumerable<TicketRun> BlockersOf(TicketRun ticket, IReadOnlyList<TicketRun> tickets, IReadOnlyList<TicketDependency> dependencies)
    {
        HashSet<TicketRunId> blockerIds = dependencies
            .Where(dependency => dependency.BlockedTicketRunId == ticket.Id)
            .Select(dependency => dependency.BlockingTicketRunId)
            .ToHashSet();
        return tickets.Where(candidate => blockerIds.Contains(candidate.Id)).OrderBy(candidate => candidate.Issue.Number);
    }
}
