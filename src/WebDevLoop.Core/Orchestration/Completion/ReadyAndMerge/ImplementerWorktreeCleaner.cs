using System.Text.Json;
using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Events;
using WebDevLoop.Core.Ports;

namespace WebDevLoop.Core.Orchestration.Completion.ReadyAndMerge;

/// <summary>
/// Workflow step 14: removes the implementer worktrees of a spec. Worktrees that are dirty or locked, that belong to a ticket
/// needing attention (resumable), or that an active agent step still uses are retained; each retained worktree is recorded
/// as a <see cref="RetainedRunEventType"/> run event and listed as a warning in <see cref="SpecWorktreesCleanedUp"/>.
/// Worktrees that are already gone are skipped silently, so repeating the cleanup is quiet.
/// </summary>
internal sealed class ImplementerWorktreeCleaner(
    ITicketRunRepository ticketRuns,
    IStepRunRepository stepRuns,
    IRunEventRepository runEvents,
    IGitWorkspace git,
    IOutbox outbox,
    IClock clock)
{
    public const string RetainedRunEventType = "WorktreeRetained";

    /// <summary>Appends the cleanup event and run events; the caller saves them.</summary>
    public async Task CleanAsync(SpecRun spec, GitRepositoryLocation location, CancellationToken cancellationToken)
    {
        IEnumerable<TicketRun> tickets = (await ticketRuns.ListBySpecRunAsync(spec.Id, cancellationToken))
            .Where(ticket => ticket.WorktreePath is not null)
            .OrderBy(ticket => ticket.Issue.Number);
        HashSet<TicketRunId> inUse = (await stepRuns.ListBySpecRunAsync(spec.Id, cancellationToken))
            .Where(step => step is { IsActive: true, TicketRunId: not null })
            .Select(step => step.TicketRunId!.Value)
            .ToHashSet();

        int removed = 0;
        var warnings = new List<string>();
        foreach (TicketRun ticket in tickets)
        {
            string path = ticket.WorktreePath!;
            if ((await git.InspectWorktreeAsync(location, path, cancellationToken)).Status == WorktreeStatus.Missing)
            {
                continue;
            }

            string? keptBecause = ticket.Status == TicketRunStatus.NeedsAttention ? "the ticket needs attention and may be resumed"
                : inUse.Contains(ticket.Id) ? "an agent step is still running in it"
                : null;
            if (keptBecause is null)
            {
                WorktreeCleanupResult cleanup = await git.CleanupWorktreeAsync(location, path, cancellationToken);
                if (cleanup.Outcome == WorktreeCleanupOutcome.Removed)
                {
                    removed++;
                }

                keptBecause = cleanup.Outcome is WorktreeCleanupOutcome.RetainedDirty or WorktreeCleanupOutcome.RetainedLocked
                    ? cleanup.Warning ?? cleanup.Outcome.ToString()
                    : null;
            }

            if (keptBecause is not null)
            {
                warnings.Add(Retain(spec, ticket, path, keptBecause));
            }
        }

        if (removed > 0 || warnings.Count > 0)
        {
            outbox.Append(new SpecWorktreesCleanedUp(spec.Id, spec.RepositoryId, removed, warnings, clock.UtcNow));
        }
    }

    private string Retain(SpecRun spec, TicketRun ticket, string path, string reason)
    {
        string warning = $"Worktree '{path}' of ticket #{ticket.Issue.Number} was retained: {reason}";
        string payload = JsonSerializer.Serialize(new { path, reason });
        runEvents.Add(RunEvent.Create(spec.Id, ticket.Id, RetainedRunEventType, payload, clock.UtcNow));
        return warning;
    }
}
