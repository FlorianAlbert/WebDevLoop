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
