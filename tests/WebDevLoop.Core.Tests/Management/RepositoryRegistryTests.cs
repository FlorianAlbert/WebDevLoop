using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Management;
using WebDevLoop.Core.Queries;
using WebDevLoop.Core.Tests.Ports.Fakes;

namespace WebDevLoop.Core.Tests.Management;

public sealed class RepositoryRegistryTests
{
    private static readonly DateTimeOffset Start = new(2026, 10, 9, 12, 0, 0, TimeSpan.Zero);
    private const int FirstRepositoryId = 0;
    private static readonly string WorkspaceRoot = Path.GetFullPath("/srv/webdevloop/workspaces");

    private readonly InMemoryWorkflowStore _store = new();
    private readonly FakeClock _clock = new(Start);
    private readonly CurrentRepositorySelection _selection = new();
    private readonly RepositoryRegistry _registry;

    public RepositoryRegistryTests() => _registry = new RepositoryRegistry(_store, _store, _store, _selection, _clock, new FixedWorkspaceRoot(WorkspaceRoot));

    [Fact]
    public async Task register_persists_an_enabled_repository_with_default_branch_and_clone_url()
    {
        CommandResult<RepositoryView> result = await _registry.RegisterAsync(new RegisterRepositoryCommand("acme", "widgets", "/work/acme/widgets"), CancellationToken.None);

        Assert.Equal(CommandStatus.Succeeded, result.Status);
        RepositoryView view = result.Value!;
        Assert.Equal(("acme", "widgets", "main", "https://github.com/acme/widgets.git", "/work/acme/widgets", true), (view.Owner, view.Name, view.DefaultBaseBranch, view.CloneUrl, view.LocalPath, view.IsEnabled));
        Assert.Equal(Start, view.CreatedAt);
        Assert.Single(await ((WebDevLoop.Core.Ports.IRepositoryRecordRepository)_store).ListAsync(CancellationToken.None));
        Assert.Equal(1, _store.SaveCount);
    }

    [Fact]
    public async Task register_without_a_local_path_derives_the_clone_path_from_the_workspace_root()
    {
        CommandResult<RepositoryView> result = await _registry.RegisterAsync(new RegisterRepositoryCommand("acme", "widgets", null), CancellationToken.None);

        Assert.Equal(CommandStatus.Succeeded, result.Status);
        Assert.Equal(Path.Combine(WorkspaceRoot, "repos", "acme", "widgets"), result.Value!.LocalPath);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public async Task register_treats_a_blank_local_path_as_not_provided(string? localPath)
    {
        CommandResult<RepositoryView> result = await _registry.RegisterAsync(new RegisterRepositoryCommand("acme", "widgets", localPath), CancellationToken.None);

        Assert.Equal(Path.Combine(WorkspaceRoot, "repos", "acme", "widgets"), result.Value!.LocalPath);
    }

    [Fact]
    public async Task register_accepts_a_repository_with_only_owner_and_name()
    {
        CommandResult<RepositoryView> result = await _registry.RegisterAsync(new RegisterRepositoryCommand("acme", "widgets"), CancellationToken.None);

        Assert.Equal(CommandStatus.Succeeded, result.Status);
    }

    [Theory]
    [InlineData("..", "widgets", "Owner")]
    [InlineData("acme", "../../etc", "Name")]
    [InlineData("a/b", "widgets", "Owner")]
    [InlineData("acme", "wid\\gets", "Name")]
    public async Task register_rejects_owner_and_name_that_cannot_form_a_safe_clone_path(string owner, string name, string expectedField)
    {
        CommandResult<RepositoryView> result = await _registry.RegisterAsync(new RegisterRepositoryCommand(owner, name), CancellationToken.None);

        Assert.Equal(CommandStatus.Invalid, result.Status);
        Assert.Contains(result.Errors!, error => error.Field == expectedField);
        Assert.Equal(0, _store.SaveCount);
    }

    [Fact]
    public async Task register_uses_explicit_branch_and_clone_url()
    {
        CommandResult<RepositoryView> result = await _registry.RegisterAsync(
            new RegisterRepositoryCommand("acme", "widgets", "/work/w", "develop", "https://example.test/w.git"), CancellationToken.None);

        Assert.Equal(("develop", "https://example.test/w.git"), (result.Value!.DefaultBaseBranch, result.Value.CloneUrl));
    }

    [Theory]
    [InlineData(null, "widgets", "/work/w", "Owner")]
    [InlineData("acme", " ", "/work/w", "Name")]
    public async Task register_rejects_missing_required_fields(string? owner, string? name, string? localPath, string expectedField)
    {
        CommandResult<RepositoryView> result = await _registry.RegisterAsync(new RegisterRepositoryCommand(owner, name, localPath), CancellationToken.None);

        Assert.Equal(CommandStatus.Invalid, result.Status);
        Assert.Contains(result.Errors!, error => error.Field == expectedField);
        Assert.Equal(0, _store.SaveCount);
    }

    [Fact]
    public async Task register_rejects_an_invalid_base_branch()
    {
        CommandResult<RepositoryView> result = await _registry.RegisterAsync(
            new RegisterRepositoryCommand("acme", "widgets", "/work/w", "bad branch"), CancellationToken.None);

        Assert.Equal(CommandStatus.Invalid, result.Status);
        Assert.Contains(result.Errors!, error => error.Field == "DefaultBaseBranch");
    }

    [Fact]
    public async Task register_rejects_a_duplicate_repository()
    {
        await _registry.RegisterAsync(new RegisterRepositoryCommand("acme", "widgets", "/work/w"), CancellationToken.None);

        CommandResult<RepositoryView> result = await _registry.RegisterAsync(new RegisterRepositoryCommand("acme", "widgets", "/work/other"), CancellationToken.None);

        Assert.Equal(CommandStatus.Conflict, result.Status);
        Assert.Equal(1, _store.SaveCount);
    }

    [Fact]
    public async Task register_reports_a_conflict_when_the_save_loses_a_race()
    {
        _store.ConflictOnNextSave = true;

        CommandResult<RepositoryView> result = await _registry.RegisterAsync(new RegisterRepositoryCommand("acme", "widgets", "/work/w"), CancellationToken.None);

        Assert.Equal(CommandStatus.Conflict, result.Status);
    }

    [Fact]
    public async Task update_changes_only_the_supplied_values()
    {
        await _registry.RegisterAsync(new RegisterRepositoryCommand("acme", "widgets", "/work/w"), CancellationToken.None);
        _clock.Advance(TimeSpan.FromMinutes(5));

        CommandResult<RepositoryView> result = await _registry.UpdateAsync(FirstRepositoryId, new UpdateRepositoryCommand(DefaultBaseBranch: "develop", IsEnabled: false), CancellationToken.None);

        Assert.Equal(CommandStatus.Succeeded, result.Status);
        Assert.Equal(("develop", "/work/w", false, Start.AddMinutes(5)), (result.Value!.DefaultBaseBranch, result.Value.LocalPath, result.Value.IsEnabled, result.Value.UpdatedAt));
    }

    [Fact]
    public async Task update_rejects_an_invalid_base_branch_without_saving()
    {
        await _registry.RegisterAsync(new RegisterRepositoryCommand("acme", "widgets", "/work/w"), CancellationToken.None);

        CommandResult<RepositoryView> result = await _registry.UpdateAsync(FirstRepositoryId, new UpdateRepositoryCommand(DefaultBaseBranch: "a..b"), CancellationToken.None);

        Assert.Equal(CommandStatus.Invalid, result.Status);
        Assert.Equal(1, _store.SaveCount);
    }

    [Fact]
    public async Task update_of_an_unknown_repository_is_not_found()
    {
        CommandResult<RepositoryView> result = await _registry.UpdateAsync(42, new UpdateRepositoryCommand(IsEnabled: false), CancellationToken.None);

        Assert.Equal(CommandStatus.NotFound, result.Status);
    }

    [Fact]
    public async Task remove_deletes_a_repository_without_runs_and_clears_it_as_current_selection()
    {
        await _registry.RegisterAsync(new RegisterRepositoryCommand("acme", "widgets", "/work/w"), CancellationToken.None);
        _selection.Select(FirstRepositoryId);

        CommandResult<int> result = await _registry.RemoveAsync(FirstRepositoryId, CancellationToken.None);

        Assert.Equal(CommandStatus.Succeeded, result.Status);
        Assert.Empty(await ((WebDevLoop.Core.Ports.IRepositoryRecordRepository)_store).ListAsync(CancellationToken.None));
        Assert.Null(_selection.CurrentRepositoryId);
    }

    [Fact]
    public async Task remove_keeps_the_selection_of_another_repository()
    {
        await _registry.RegisterAsync(new RegisterRepositoryCommand("acme", "widgets", "/work/w"), CancellationToken.None);
        _selection.Select(7);

        await _registry.RemoveAsync(FirstRepositoryId, CancellationToken.None);

        Assert.Equal(7, _selection.CurrentRepositoryId);
    }

    [Fact]
    public async Task remove_is_rejected_while_the_repository_has_spec_runs()
    {
        await _registry.RegisterAsync(new RegisterRepositoryCommand("acme", "widgets", "/work/w"), CancellationToken.None);
        ((WebDevLoop.Core.Ports.ISpecRunRepository)_store).Add(SpecRun.Queue(new RunId("run-1"), FirstRepositoryId, new IssueRef("acme", "widgets", 1), "t", "b", 1, Start));

        CommandResult<int> result = await _registry.RemoveAsync(FirstRepositoryId, CancellationToken.None);

        Assert.Equal(CommandStatus.Conflict, result.Status);
        Assert.Single(await ((WebDevLoop.Core.Ports.IRepositoryRecordRepository)_store).ListAsync(CancellationToken.None));
    }

    [Fact]
    public async Task remove_of_an_unknown_repository_is_not_found()
    {
        CommandResult<int> result = await _registry.RemoveAsync(42, CancellationToken.None);

        Assert.Equal(CommandStatus.NotFound, result.Status);
    }
}

internal sealed class FixedWorkspaceRoot(string root) : IWorkspaceRootProvider
{
    public Task<string> GetAsync(CancellationToken cancellationToken) => Task.FromResult(root);
}
