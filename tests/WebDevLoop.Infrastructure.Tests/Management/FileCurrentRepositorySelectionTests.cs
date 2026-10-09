using WebDevLoop.Infrastructure.Management;

namespace WebDevLoop.Infrastructure.Tests.Management;

public sealed class FileCurrentRepositorySelectionTests : IDisposable
{
    private readonly TestDirectory _directory = new("ui-state");

    private string StatePath => Path.Combine(_directory.Path, "nested", "ui-state.json");

    public void Dispose() => _directory.Dispose();

    [Fact]
    public void Nothing_is_selected_before_the_first_selection()
    {
        Assert.Null(new FileCurrentRepositorySelection(StatePath).CurrentRepositoryId);
    }

    [Fact]
    public void Selection_survives_a_restart()
    {
        new FileCurrentRepositorySelection(StatePath).Select(42);

        Assert.Equal(42, new FileCurrentRepositorySelection(StatePath).CurrentRepositoryId);
    }

    [Fact]
    public void Clearing_the_selection_is_persisted_too()
    {
        var selection = new FileCurrentRepositorySelection(StatePath);
        selection.Select(42);

        selection.Select(null);

        Assert.Null(selection.CurrentRepositoryId);
        Assert.Null(new FileCurrentRepositorySelection(StatePath).CurrentRepositoryId);
    }

    [Fact]
    public void An_unreadable_state_file_starts_without_a_selection()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(StatePath)!);
        File.WriteAllText(StatePath, "{ not json");

        Assert.Null(new FileCurrentRepositorySelection(StatePath).CurrentRepositoryId);
    }
}
