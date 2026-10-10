using System.Text.Json;
using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Orchestration.Preparation;
using WebDevLoop.Core.Orchestration.TicketExecution;
using WebDevLoop.Core.Ports;
using WebDevLoop.Core.Tests.Orchestration.ReviewLoop;
using WebDevLoop.Core.Tests.Ports.Fakes;

namespace WebDevLoop.Core.Tests.Orchestration.TicketExecution;

public sealed class WorktreeRemediatorTests : IDisposable
{
    private static readonly RunId Run = new("run-1");
    private static readonly TicketRunId Ticket = new("t-1");
    private static readonly GitRepositoryLocation Location = new(new GitHubRepoRef("octo", "app"), "https://example.test/app.git", "/clones/app");
    private readonly string _workspaceRoot = Path.Combine(Path.GetTempPath(), "wdl-remediator-" + Guid.NewGuid().ToString("N"));
    private readonly InMemoryGitWorkspace _git = new();
    private readonly RecordingRunEvents _events = new();
    private readonly WorktreeRemediator _remediator;
    private readonly RunWorkspaceLayout _layout;
    private readonly string _worktreePath;

    public WorktreeRemediatorTests()
    {
        _layout = RunWorkspaceLayout.For(_workspaceRoot, Run);
        _worktreePath = Path.Combine(_layout.RunDirectory, "tickets", Ticket.Value);
        _remediator = new WorktreeRemediator(_git, _events, new FakeClock(new DateTimeOffset(2026, 1, 1, 10, 0, 0, TimeSpan.Zero)));
        CommitSha start = _git.Commit([], "README.md");
        _git.PrepareWorktreeAsync(Location, new WorktreeSpec(new BranchName("webdevloop/run-1/ticket/t-1"), start, _worktreePath), TestContext.Current.CancellationToken)
            .GetAwaiter().GetResult();
    }

    [Fact]
    public async Task an_untracked_artefact_is_removed_and_recorded_without_a_patch()
    {
        _git.SetWorktreeChanges(_worktreePath, new WorktreeChanges(string.Empty, [], ["__pycache__/calc.pyc"], []));

        WorktreeRemediation result = await RemediateAsync();

        Assert.True(result.Performed);
        Assert.Null(result.PatchPath);
        Assert.Equal(["__pycache__/calc.pyc"], result.RemovedPaths);
        Assert.Equal(WorktreeStatus.Clean, (await _git.InspectWorktreeAsync(Location, _worktreePath, TestContext.Current.CancellationToken)).Status);
        Assert.False(Directory.Exists(_layout.WorktreeBackupsDirectory));
        RunEvent runEvent = Assert.Single(_events.All);
        Assert.Equal(WorktreeRemediator.RunEventType, runEvent.Type);
        Assert.Equal(Run, runEvent.SpecRunId);
        Assert.Equal(Ticket, runEvent.TicketRunId);
        using JsonDocument payload = JsonDocument.Parse(runEvent.PayloadJson);
        Assert.Equal("__pycache__/calc.pyc", payload.RootElement.GetProperty("removedPaths")[0].GetString());
    }

    [Fact]
    public async Task tracked_changes_are_saved_as_a_patch_in_the_run_folder_before_the_worktree_is_reset()
    {
        const string Patch = "--- a/calc.py\n+++ b/calc.py\n@@ -1 +1 @@\n-return a + b\n+return a - b\n";
        _git.SetWorktreeChanges(_worktreePath, new WorktreeChanges(Patch, ["calc.py"], ["scratch.txt"], []));

        WorktreeRemediation result = await RemediateAsync();

        Assert.NotNull(result.PatchPath);
        Assert.StartsWith(Path.Combine(_layout.WorktreeBackupsDirectory, Ticket.Value), result.PatchPath, StringComparison.Ordinal);
        Assert.Equal(Patch, await File.ReadAllTextAsync(result.PatchPath, TestContext.Current.CancellationToken));
        Assert.Equal(["calc.py"], result.TrackedFiles);
        Assert.Equal(WorktreeStatus.Clean, (await _git.InspectWorktreeAsync(Location, _worktreePath, TestContext.Current.CancellationToken)).Status);
        using JsonDocument payload = JsonDocument.Parse(Assert.Single(_events.All).PayloadJson);
        Assert.Equal(result.PatchPath, payload.RootElement.GetProperty("patchPath").GetString());
    }

    [Fact]
    public async Task repeated_remediations_never_overwrite_an_earlier_patch()
    {
        _git.SetWorktreeChanges(_worktreePath, new WorktreeChanges("first", ["a.py"], [], []));
        WorktreeRemediation first = await RemediateAsync();
        _git.SetWorktreeChanges(_worktreePath, new WorktreeChanges("second", ["a.py"], [], []));
        WorktreeRemediation second = await RemediateAsync();

        Assert.NotEqual(first.PatchPath, second.PatchPath);
        Assert.Equal("first", await File.ReadAllTextAsync(first.PatchPath!, TestContext.Current.CancellationToken));
        Assert.Equal("second", await File.ReadAllTextAsync(second.PatchPath!, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task a_clean_worktree_is_left_alone()
    {
        WorktreeRemediation result = await RemediateAsync();

        Assert.False(result.Performed);
        Assert.Empty(_events.All);
    }

    [Fact]
    public async Task a_missing_worktree_is_left_alone()
    {
        _git.DeleteWorktree(_worktreePath);

        Assert.False((await RemediateAsync()).Performed);
        Assert.Empty(_events.All);
    }

    public void Dispose()
    {
        if (Directory.Exists(_workspaceRoot))
        {
            Directory.Delete(_workspaceRoot, recursive: true);
        }
    }

    private Task<WorktreeRemediation> RemediateAsync() =>
        _remediator.RemediateAsync(Run, Ticket, Location, _layout, _worktreePath, TestContext.Current.CancellationToken);
}
