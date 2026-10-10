using System.Globalization;
using System.Text.Json;
using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Orchestration.Preparation;
using WebDevLoop.Core.Ports;

namespace WebDevLoop.Core.Orchestration.TicketExecution;

/// <summary>
/// Automatic remediation for a dirty ticket worktree. WebDevLoop owns these worktrees, and agents or the tests they run
/// routinely leave build and test artefacts (for example <c>__pycache__</c>) behind, so a dirty status is not a reason to stop:
/// tracked changes are saved as a patch in the run folder, then the worktree is reset to its commit and every untracked or
/// ignored file inside it is removed. The reviewed commit stays the source of truth; nothing is committed on the agent's behalf.
/// Appends a <see cref="RunEventType"/> run event (saved with the caller's next unit of work) describing what was done.
/// </summary>
public sealed class WorktreeRemediator(IGitWorkspace git, IRunEventRepository runEvents, IClock clock)
{
    public const string RunEventType = "WorktreeRemediated";

    private const string PatchFileSuffix = "-tracked-changes.patch";

    /// <summary>Cleans the worktree when it is dirty; a clean, locked or missing worktree is left alone.</summary>
    public async Task<WorktreeRemediation> RemediateAsync(
        RunId specRunId,
        TicketRunId ticketRunId,
        GitRepositoryLocation location,
        RunWorkspaceLayout layout,
        string worktreePath,
        CancellationToken cancellationToken)
    {
        WorktreeInspection inspection = await git.InspectWorktreeAsync(location, worktreePath, cancellationToken);
        if (inspection.Status != WorktreeStatus.Dirty)
        {
            return WorktreeRemediation.NotNeeded;
        }

        WorktreeChanges changes = await git.GetWorktreeChangesAsync(location, worktreePath, cancellationToken);
        string? patchPath = changes.HasTrackedChanges
            ? await SavePatchAsync(layout, ticketRunId, changes.TrackedPatch, cancellationToken)
            : null;

        WorktreeCleanResult cleaned = await git.CleanWorktreeAsync(location, worktreePath, cancellationToken);
        var remediation = new WorktreeRemediation(true, patchPath, changes.TrackedFiles, cleaned.RemovedPaths);
        string payload = JsonSerializer.Serialize(new
        {
            worktreePath,
            patchPath,
            trackedFiles = remediation.TrackedFiles,
            removedPaths = remediation.RemovedPaths,
        });
        runEvents.Add(RunEvent.Create(specRunId, ticketRunId, RunEventType, payload, clock.UtcNow));
        return remediation;
    }

    private async Task<string> SavePatchAsync(RunWorkspaceLayout layout, TicketRunId ticketRunId, string patch, CancellationToken cancellationToken)
    {
        string directory = Path.Combine(layout.WorktreeBackupsDirectory, ticketRunId.Value);
        Directory.CreateDirectory(directory);
        string stamp = clock.UtcNow.ToString("yyyyMMddTHHmmssfff", CultureInfo.InvariantCulture);
        string path = Path.Combine(directory, stamp + PatchFileSuffix);
        for (int copy = 2; File.Exists(path); copy++)
        {
            path = Path.Combine(directory, $"{stamp}-{copy}{PatchFileSuffix}");
        }

        await File.WriteAllTextAsync(path, patch, cancellationToken);
        return path;
    }
}

/// <param name="Performed">False when the worktree was not dirty and nothing was touched.</param>
/// <param name="PatchPath">Backup of the discarded tracked changes; null when there were none.</param>
public sealed record WorktreeRemediation(
    bool Performed,
    string? PatchPath,
    IReadOnlyList<string> TrackedFiles,
    IReadOnlyList<string> RemovedPaths)
{
    public static WorktreeRemediation NotNeeded { get; } = new(false, null, [], []);
}
