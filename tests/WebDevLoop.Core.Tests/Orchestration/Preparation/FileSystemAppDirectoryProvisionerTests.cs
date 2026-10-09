using WebDevLoop.Core.Orchestration.Preparation;

namespace WebDevLoop.Core.Tests.Orchestration.Preparation;

public sealed class FileSystemAppDirectoryProvisionerTests : IDisposable
{
    private readonly string _sandbox = Path.Combine(AppContext.BaseDirectory, "provisioner-sandbox", Guid.NewGuid().ToString("N"));

    [Fact]
    public void creates_missing_nested_directories_idempotently()
    {
        string notes = Path.Combine(_sandbox, "runs", "run1", "notes");
        var provisioner = new FileSystemAppDirectoryProvisioner();

        provisioner.EnsureExists(notes);
        provisioner.EnsureExists(notes);

        Assert.True(Directory.Exists(notes));
    }

    [Fact]
    public void relative_paths_are_rejected()
    {
        var provisioner = new FileSystemAppDirectoryProvisioner();

        Assert.Throws<ArgumentException>(() => provisioner.EnsureExists("relative/notes"));
    }

    public void Dispose()
    {
        if (Directory.Exists(_sandbox))
        {
            Directory.Delete(_sandbox, recursive: true);
        }
    }
}
