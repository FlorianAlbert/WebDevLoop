using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Orchestration.Preparation;
using WebDevLoop.Core.Orchestration.SpecQueue;
using WebDevLoop.Core.Orchestration.TicketExecution;
using WebDevLoop.Core.Ports;
using WebDevLoop.Core.Settings;

namespace WebDevLoop.Core.Orchestration.Attention;

/// <summary>What a remediation needs to touch the repository of a parked run or ticket.</summary>
public sealed record AttentionWork(
    SpecRun Spec,
    TicketRun? Ticket,
    RepositoryRecord Repository,
    EffectiveSettings Settings,
    GitRepositoryLocation Location,
    RunWorkspaceLayout Layout)
{
    public string? TicketWorktreePath => Ticket is null ? null : TicketWorktreeLayout.PathFor(Settings.WorkspaceRootDirectory, Spec.Id, Ticket.Id);
}

public sealed class AttentionWorkLoader(
    ISpecRunRepository specRuns,
    ITicketRunRepository ticketRuns,
    IRepositoryRecordRepository repositories,
    IEffectiveSettingsProvider settings)
{
    /// <returns>Null when the run, its ticket or its repository no longer exists.</returns>
    public async Task<AttentionWork?> LoadAsync(AttentionCase attentionCase, CancellationToken cancellationToken)
    {
        if (await specRuns.GetAsync(attentionCase.SpecRunId, cancellationToken) is not { } spec
            || await repositories.GetAsync(spec.RepositoryId, cancellationToken) is not { } repository)
        {
            return null;
        }

        TicketRun? ticket = null;
        if (attentionCase.TicketRunId is { } ticketId)
        {
            ticket = await ticketRuns.GetAsync(ticketId, cancellationToken);
            if (ticket is null)
            {
                return null;
            }
        }

        EffectiveSettings effective = await settings.GetAsync(spec.RepositoryId, cancellationToken);
        return new AttentionWork(spec, ticket, repository, effective, GitRepositoryLocation.From(repository), RunWorkspaceLayout.For(effective.WorkspaceRootDirectory, spec.Id));
    }
}
