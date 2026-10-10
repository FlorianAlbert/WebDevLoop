using System.Globalization;
using System.Text;
using WebDevLoop.Core.Agents;
using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Ports;
using WebDevLoop.Core.Queries;

namespace WebDevLoop.Core.Orchestration.Attention;

/// <summary>What the troubleshooter session is given: its worktrees, the read-only context and the backups taken before it starts.</summary>
/// <param name="IntegrationWorktree">Scratch checkout of the integration tip; null when the integration branch does not exist locally.</param>
public sealed record TroubleshooterWorkspaceInfo(
    string TicketWorktree,
    string? IntegrationWorktree,
    string ContextDirectory,
    string BackupDirectory,
    string LogTail,
    IReadOnlyList<string> BackedUp)
{
    public AgentWorkspace ToAgentWorkspace(IReadOnlyList<string> protectedBranches) => new(
        TicketWorktree,
        ContextDirectory,
        [.. IntegrationWorktree is null ? [] : new[] { IntegrationWorktree }, BackupDirectory],
        protectedBranches);
}

/// <summary>
/// Prepares everything the troubleshooter session needs outside the agent: the ticket worktree exists, the scratch integration
/// checkout is reset to the current tip, uncommitted changes and the branch tips are saved in the run folder (so nothing the agent
/// does is lost silently), and the problem, the git state and the log tail are written for the agent to read.
/// </summary>
public sealed class TroubleshooterWorkspace(IGitWorkspace git, IStepRunRepository steps, IAgentLogReader logs, IClock clock, TroubleshooterOptions options)
{
    private const int MaxLogLineLength = 400;
    private const int MaxLogTailLength = 6000;

    /// <returns>Null when the ticket worktree cannot be provided because its branch is missing.</returns>
    public async Task<TroubleshooterWorkspaceInfo?> PrepareAsync(
        AttentionWork work,
        AttentionCase attentionCase,
        TroubleshootingState state,
        int attempt,
        CancellationToken cancellationToken)
    {
        TicketRun ticket = work.Ticket!;
        string path = work.TicketWorktreePath!;
        if (state.Worktree.Status == WorktreeStatus.Missing)
        {
            if (state.BranchTip is not { } tip)
            {
                return null;
            }

            await git.PrepareWorktreeAsync(work.Location, new WorktreeSpec(ticket.BranchName, tip, path), cancellationToken);
        }

        string stamp = clock.UtcNow.ToString("yyyyMMddTHHmmssfff", CultureInfo.InvariantCulture);
        string backups = Directory.CreateDirectory(Path.Combine(work.Layout.TroubleshooterBackupsDirectory, ticket.Id.Value, $"{stamp}-attempt-{attempt}")).FullName;
        string context = Directory.CreateDirectory(Path.Combine(work.Layout.TroubleshooterContextDirectory, ticket.Id.Value, $"attempt-{attempt}")).FullName;

        IReadOnlyList<string> backedUp = await BackUpAsync(work, state, backups, cancellationToken);
        string? integrationWorktree = await PrepareIntegrationWorktreeAsync(work, state, cancellationToken);
        string logTail = await ReadLogTailAsync(ticket, cancellationToken);

        await File.WriteAllTextAsync(Path.Combine(context, "problem.json"), attentionCase.Reason.ToJson(), cancellationToken);
        await File.WriteAllTextAsync(Path.Combine(context, "git-state.txt"), state.GitText, cancellationToken);
        await File.WriteAllTextAsync(Path.Combine(context, "github-state.txt"), state.GitHubText, cancellationToken);
        await File.WriteAllTextAsync(Path.Combine(context, "agent-log-tail.txt"), logTail, cancellationToken);
        return new TroubleshooterWorkspaceInfo(path, integrationWorktree, context, backups, logTail, backedUp);
    }

    /// <summary>Removes the scratch integration checkout again; a dirty one is retained by the git port with a warning.</summary>
    public async Task ReleaseAsync(AttentionWork work, TroubleshooterWorkspaceInfo info, CancellationToken cancellationToken)
    {
        if (info.IntegrationWorktree is { } path)
        {
            try
            {
                await git.CleanupWorktreeAsync(work.Location, path, cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // The scratch checkout is disposable; a failed cleanup must not change the outcome of the session.
            }
        }
    }

    private async Task<IReadOnlyList<string>> BackUpAsync(AttentionWork work, TroubleshootingState state, string directory, CancellationToken cancellationToken)
    {
        var written = new List<string>();
        if (state.Changes.HasTrackedChanges)
        {
            string patch = Path.Combine(directory, "tracked-changes.patch");
            await File.WriteAllTextAsync(patch, state.Changes.TrackedPatch, cancellationToken);
            written.Add(patch);
        }

        if (state.Changes.UntrackedFiles.Count > 0)
        {
            string list = Path.Combine(directory, "untracked-files.txt");
            await File.WriteAllLinesAsync(list, state.Changes.UntrackedFiles, cancellationToken);
            written.Add(list);
        }

        string refs = Path.Combine(directory, "refs.txt");
        var text = new StringBuilder();
        text.AppendLine(CultureInfo.InvariantCulture, $"{work.Ticket!.BranchName} {state.BranchTip?.Value ?? "(missing)"}");
        text.AppendLine(CultureInfo.InvariantCulture, $"{work.Spec.IntegrationBranch} {state.IntegrationTip?.Value ?? "(missing)"}");
        text.AppendLine(CultureInfo.InvariantCulture, $"HEAD of {work.TicketWorktreePath} {state.Worktree.Head?.Value ?? "(missing)"}");
        await File.WriteAllTextAsync(refs, text.ToString(), cancellationToken);
        written.Add(refs);
        return written;
    }

    private async Task<string?> PrepareIntegrationWorktreeAsync(AttentionWork work, TroubleshootingState state, CancellationToken cancellationToken)
    {
        if (state.IntegrationTip is not { } tip)
        {
            return null;
        }

        string path = work.Layout.TroubleshooterIntegrationWorktree;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await git.PrepareWorktreeAsync(work.Location, new WorktreeSpec(work.Layout.TroubleshooterBranch, tip, path), cancellationToken);
        return path;
    }

    private async Task<string> ReadLogTailAsync(TicketRun ticket, CancellationToken cancellationToken)
    {
        StepRun? last = (await steps.ListByTicketRunAsync(ticket.Id, cancellationToken))
            .Where(step => step.Kind != StepKind.Troubleshoot && step.StartedAt is not null)
            .OrderByDescending(step => step.StartedAt)
            .FirstOrDefault();
        if (last is null)
        {
            return "(the ticket has no earlier agent step)";
        }

        IReadOnlyList<AgentLogView> entries = await logs.ReadAsync(last.Id, 0, cancellationToken);
        var text = new StringBuilder();
        text.AppendLine(CultureInfo.InvariantCulture, $"Step {last.Id} ({last.Kind}, {last.AgentRole}, {last.Status}{(last.FailureReason is null ? string.Empty : ": " + last.FailureReason)})");
        foreach (AgentLogView entry in entries.TakeLast(options.LogTailEntries))
        {
            string line = entry.Text.ReplaceLineEndings(" ");
            text.AppendLine(CultureInfo.InvariantCulture, $"[{entry.Kind}] {(line.Length > MaxLogLineLength ? line[..MaxLogLineLength] + "…" : line)}");
        }

        string tail = text.ToString().TrimEnd();
        return tail.Length > MaxLogTailLength ? "…" + tail[^MaxLogTailLength..] : tail;
    }
}
