using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Orchestration.Preparation;
using WebDevLoop.Core.Ports;
using WebDevLoop.Core.Settings;

namespace WebDevLoop.Core.Orchestration.TicketExecution;

/// <summary>Everything one implementation run of a ticket needs, loaded once per run.</summary>
internal sealed record ImplementationContext(
    SpecRun Spec,
    TicketRun Ticket,
    RepositoryRecord Repository,
    EffectiveSettings Settings,
    GitRepositoryLocation Location,
    RunWorkspaceLayout Layout)
{
    public string WorktreePath { get; } = TicketWorktreeLayout.PathFor(Settings.WorkspaceRootDirectory, Spec.Id, Ticket.Id);
}
