using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Orchestration.Preparation;

namespace WebDevLoop.Core.Orchestration.Completion.Testing;

/// <summary>
/// The checkout of the integration tip the tester builds and runs the application from: a run-scoped directory beside the
/// exploration notes (never inside the clone) on its own run-scoped branch, so the integration branch is never checked out.
/// </summary>
public sealed class TestWorkspace
{
    private const string CheckoutDirectoryName = "test";

    private TestWorkspace(string checkoutDirectory, BranchName branch, string notesDirectory)
    {
        CheckoutDirectory = checkoutDirectory;
        Branch = branch;
        NotesDirectory = notesDirectory;
    }

    public string CheckoutDirectory { get; }

    public BranchName Branch { get; }

    /// <summary>The run's exploration notes directory; the tester stores its evidence below it.</summary>
    public string NotesDirectory { get; }

    public static TestWorkspace For(string workspaceRoot, RunId runId)
    {
        RunWorkspaceLayout layout = RunWorkspaceLayout.For(workspaceRoot, runId);
        return new TestWorkspace(
            Path.Combine(layout.RunDirectory, CheckoutDirectoryName), new BranchName($"webdevloop/{runId}/test"), layout.ExplorationNotesDirectory);
    }
}
