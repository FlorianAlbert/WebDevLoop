using WebDevLoop.Infrastructure.Persistence;

namespace WebDevLoop.Infrastructure.Tests.Persistence;

public sealed class DatabaseInstanceLockTests : IDisposable
{
    private readonly TestDirectory _directory = new("instance-lock");

    public void Dispose() => _directory.Dispose();

    private string DatabasePath(string name = "webdevloop.db") => Path.Combine(_directory.Path, name);

    [Fact]
    public void a_second_instance_on_the_same_database_is_refused_with_a_message_that_names_the_database_and_the_way_out()
    {
        using DatabaseInstanceLock first = DatabaseInstanceLock.Acquire(DatabasePath());

        DatabaseInUseException refused = Assert.Throws<DatabaseInUseException>(() => DatabaseInstanceLock.Acquire(DatabasePath()));

        Assert.Contains(DatabasePath(), refused.Message, StringComparison.Ordinal);
        Assert.Contains("WebDevLoop__DatabasePath", refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void the_database_can_be_acquired_again_once_the_holder_is_disposed()
    {
        DatabaseInstanceLock.Acquire(DatabasePath()).Dispose();

        using DatabaseInstanceLock second = DatabaseInstanceLock.Acquire(DatabasePath());
    }

    [Fact]
    public void instances_with_different_databases_do_not_block_each_other()
    {
        using DatabaseInstanceLock first = DatabaseInstanceLock.Acquire(DatabasePath("one.db"));
        using DatabaseInstanceLock second = DatabaseInstanceLock.Acquire(DatabasePath("two.db"));
    }

    [Fact]
    public void the_database_directory_is_created_when_it_does_not_exist_yet()
    {
        string path = Path.Combine(_directory.Path, "missing", "webdevloop.db");

        using DatabaseInstanceLock held = DatabaseInstanceLock.Acquire(path);

        Assert.True(Directory.Exists(Path.GetDirectoryName(path)));
    }
}
