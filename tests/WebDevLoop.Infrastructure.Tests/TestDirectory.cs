namespace WebDevLoop.Infrastructure.Tests;

/// <summary>A unique scratch directory under the test output folder, deleted on dispose.</summary>
internal sealed class TestDirectory : IDisposable
{
    public TestDirectory(string prefix)
    {
        Path = System.IO.Path.Combine(AppContext.BaseDirectory, "test-scratch", $"{prefix}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public void Dispose() => Directory.Delete(Path, recursive: true);
}
