using WebDevLoop.Core.Domain;

namespace WebDevLoop.Core.Orchestration.Preparation;

/// <summary>
/// App-owned, run-scoped directories under the workspace root: <c>&lt;root&gt;/runs/&lt;run-id&gt;/…</c>. They are siblings of,
/// never inside, repository clones and ticket worktrees, so agents can share them without touching a checkout.
/// </summary>
public sealed class RunWorkspaceLayout
{
    private const string RunsDirectoryName = "runs";
    private const string NotesDirectoryName = "notes";
    private const string ExplorerCheckoutDirectoryName = "explore";
    private const string WorktreeBackupsDirectoryName = "worktree-backups";
    private const string TroubleshooterDirectoryName = "troubleshooter";

    private RunWorkspaceLayout(RunId runId, string runDirectory)
    {
        RunId = runId;
        RunDirectory = runDirectory;
    }

    public RunId RunId { get; }

    public string RunDirectory { get; }

    /// <summary>Exploration notes shared with every later agent of the run (<c>{exploration_notes_path}</c>).</summary>
    public string ExplorationNotesDirectory => Path.Combine(RunDirectory, NotesDirectoryName);

    /// <summary>Read-only checkout of the integration tip the explorer works in.</summary>
    public string ExplorerCheckoutDirectory => Path.Combine(RunDirectory, ExplorerCheckoutDirectoryName);

    /// <summary>Patches of tracked changes removed from ticket worktrees, one subfolder per ticket.</summary>
    public string WorktreeBackupsDirectory => Path.Combine(RunDirectory, WorktreeBackupsDirectoryName);

    /// <summary>Everything the troubleshooter uses: its context files, backups and the scratch checkout of the integration branch.</summary>
    public string TroubleshooterDirectory => Path.Combine(RunDirectory, TroubleshooterDirectoryName);

    /// <summary>Scratch checkout of the integration tip the troubleshooter may inspect and modify; it is on its own branch.</summary>
    public string TroubleshooterIntegrationWorktree => Path.Combine(TroubleshooterDirectory, "integration");

    /// <summary>Patches and ref listings saved before a troubleshooter session; the agent may add its own.</summary>
    public string TroubleshooterBackupsDirectory => Path.Combine(TroubleshooterDirectory, "backups");

    /// <summary>Read-only for the agent: the problem, git state and log tail of each attempt.</summary>
    public string TroubleshooterContextDirectory => Path.Combine(TroubleshooterDirectory, "context");

    public BranchName TroubleshooterBranch => new($"webdevloop/{RunId}/troubleshoot");

    public BranchName ExplorerBranch => new($"webdevloop/{RunId}/explore");

    public static RunWorkspaceLayout For(string workspaceRoot, RunId runId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);
        if (!Path.IsPathFullyQualified(workspaceRoot))
        {
            throw new ArgumentException($"Workspace root '{workspaceRoot}' must be an absolute path.", nameof(workspaceRoot));
        }

        return new RunWorkspaceLayout(runId, Path.Combine(Path.GetFullPath(workspaceRoot), RunsDirectoryName, runId.Value));
    }
}
