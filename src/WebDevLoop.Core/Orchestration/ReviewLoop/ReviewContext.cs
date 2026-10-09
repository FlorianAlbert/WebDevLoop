using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Orchestration.Preparation;
using WebDevLoop.Core.Settings;

namespace WebDevLoop.Core.Orchestration.ReviewLoop;

/// <summary>Everything one review round needs, loaded once per round.</summary>
internal sealed record ReviewContext(
    ReviewRequest Request,
    SpecRun Spec,
    TicketRun? Ticket,
    RepositoryRecord Repository,
    EffectiveSettings Settings)
{
    public required CommitSha IntegrationTip { get; init; }

    public required IReadOnlyList<string> ChangedFiles { get; init; }

    public string NotesDirectory => RunWorkspaceLayout.For(Settings.WorkspaceRootDirectory, Spec.Id).ExplorationNotesDirectory;
}
