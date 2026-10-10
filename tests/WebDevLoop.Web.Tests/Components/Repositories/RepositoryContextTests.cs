using WebDevLoop.Core.Management;
using WebDevLoop.Web.Components.Repositories;

namespace WebDevLoop.Web.Tests.Components.Repositories;

public sealed class RepositoryContextTests
{
    private readonly CurrentRepositorySelection _selection = new();
    private readonly RepositoryContext _context;
    private int _changes;

    public RepositoryContextTests()
    {
        _context = new RepositoryContext(_selection);
        _context.Changed += () => _changes++;
    }

    [Fact]
    public void select_updates_the_shared_selection_and_notifies()
    {
        _context.Select(7);

        Assert.Equal(7, _selection.CurrentRepositoryId);
        Assert.Equal(7, _context.CurrentRepositoryId);
        Assert.Equal(1, _changes);
    }

    [Fact]
    public void select_null_clears_the_selection()
    {
        _context.Select(7);

        _context.Select(null);

        Assert.Null(_context.CurrentRepositoryId);
        Assert.Equal(2, _changes);
    }

    [Fact]
    public void reflects_a_selection_made_elsewhere_without_notifying()
    {
        _selection.Select(3);

        Assert.Equal(3, _context.CurrentRepositoryId);
        Assert.Equal(0, _changes);
    }

    [Fact]
    public void notify_changed_raises_the_event_for_repository_list_changes()
    {
        _context.NotifyChanged();

        Assert.Equal(1, _changes);
    }

    [Fact]
    public void clear_if_dangling_clears_a_selection_that_points_at_a_missing_repository_and_notifies()
    {
        _selection.Select(4);

        bool cleared = _context.ClearIfDangling([1, 2]);

        Assert.True(cleared);
        Assert.Null(_selection.CurrentRepositoryId);
        Assert.Equal(1, _changes);
    }

    [Fact]
    public void clear_if_dangling_keeps_a_valid_or_empty_selection()
    {
        _selection.Select(2);
        Assert.False(_context.ClearIfDangling([1, 2]));
        Assert.Equal(2, _selection.CurrentRepositoryId);

        _selection.Select(null);
        Assert.False(_context.ClearIfDangling([]));
        Assert.Equal(0, _changes);
    }
}
