using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Orchestration.Preparation;
using WebDevLoop.Core.Orchestration.TicketExecution;
using WebDevLoop.Core.Ports;
using WebDevLoop.Core.Settings;

namespace WebDevLoop.Core.Orchestration.Integration;

/// <summary>Everything one integration saga run needs, loaded once inside the repository merge lock.</summary>
internal sealed record IntegrationContext(
    SpecRun Spec,
    TicketRun Ticket,
    RepositoryRecord Repository,
    EffectiveSettings Settings,
    IntegrationSaga Saga)
{
    public GitRepositoryLocation Location { get; } = GitRepositoryLocation.From(Repository);

    public RunWorkspaceLayout Layout => RunWorkspaceLayout.For(Settings.WorkspaceRootDirectory, Spec.Id);

    public string WorktreePath => Ticket.WorktreePath ?? TicketWorktreeLayout.PathFor(Settings.WorkspaceRootDirectory, Spec.Id, Ticket.Id);

    public BranchName Trunk => Spec.BaseBranch ?? Settings.BaseBranch;

    /// <summary>The integration tip the ticket's squash commit is parented on.</summary>
    public CommitSha ExpectedPrior => Saga.ExpectedPriorIntegrationSha
        ?? throw new InvalidOperationException($"Integration saga of ticket '{Ticket.Id}' has no expected prior integration tip.");

    public CommitSha SquashCommit => Saga.SquashCommitSha
        ?? throw new InvalidOperationException($"Integration saga of ticket '{Ticket.Id}' has no squash commit yet.");
}
