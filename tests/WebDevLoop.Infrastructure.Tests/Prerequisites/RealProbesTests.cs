using Microsoft.EntityFrameworkCore;
using WebDevLoop.Infrastructure.Persistence;
using WebDevLoop.Infrastructure.Prerequisites;

namespace WebDevLoop.Infrastructure.Tests.Prerequisites;

public sealed class RealProbesTests : IDisposable
{
    private readonly TestDirectory _directory = new("probes");

    public void Dispose() => _directory.Dispose();

    [Fact]
    public async Task process_probe_reports_a_missing_executable_as_not_found()
    {
        var probe = new ProcessProbe(TimeSpan.FromSeconds(5));

        ProcessProbeResult result = await probe.RunAsync("webdevloop-no-such-tool", ["--version"], CancellationToken.None);

        Assert.Equal(ProcessProbeOutcome.NotFound, result.Outcome);
        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task process_probe_captures_exit_code_and_output()
    {
        var probe = new ProcessProbe(TimeSpan.FromSeconds(30));

        ProcessProbeResult ok = await probe.RunAsync("dotnet", ["--version"], CancellationToken.None);
        ProcessProbeResult failing = await probe.RunAsync("dotnet", ["webdevloop-no-such-command"], CancellationToken.None);

        Assert.True(ok.Succeeded);
        Assert.False(string.IsNullOrWhiteSpace(ok.Output));
        Assert.Equal(ProcessProbeOutcome.Completed, failing.Outcome);
        Assert.NotEqual(0, failing.ExitCode);
    }

    [Fact]
    public async Task process_probe_times_out_and_stops_a_hanging_tool()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Uses the POSIX sleep command.");
        var probe = new ProcessProbe(TimeSpan.FromMilliseconds(300));

        ProcessProbeResult result = await probe.RunAsync("sleep", ["30"], CancellationToken.None);

        Assert.Equal(ProcessProbeOutcome.TimedOut, result.Outcome);
    }

    [Fact]
    public async Task process_probe_propagates_caller_cancellation()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Uses the POSIX sleep command.");
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));
        var probe = new ProcessProbe(TimeSpan.FromSeconds(30));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => probe.RunAsync("sleep", ["30"], cts.Token));
    }

    [Fact]
    public void file_system_probe_creates_a_missing_writable_directory_and_leaves_no_files_behind()
    {
        string directory = Path.Combine(_directory.Path, "nested", "workspace");

        string? problem = new FileSystemProbe().TryEnsureWritableDirectory(directory);

        Assert.Null(problem);
        Assert.Empty(Directory.EnumerateFileSystemEntries(directory));
    }

    [Fact]
    public void file_system_probe_reports_a_directory_that_cannot_be_created()
    {
        string blocker = Path.Combine(_directory.Path, "blocker");
        File.WriteAllText(blocker, "not a directory");

        string? problem = new FileSystemProbe().TryEnsureWritableDirectory(Path.Combine(blocker, "workspace"));

        Assert.False(string.IsNullOrWhiteSpace(problem));
    }

    [Fact]
    public void file_system_probe_distinguishes_existing_files()
    {
        string file = Path.Combine(_directory.Path, "copilot");
        File.WriteAllText(file, "x");
        var probe = new FileSystemProbe();

        Assert.True(probe.FileExists(file));
        Assert.False(probe.FileExists(Path.Combine(_directory.Path, "missing")));
        Assert.False(probe.FileExists(_directory.Path));
    }

    [Fact]
    public async Task database_probe_lists_pending_migrations_of_a_fresh_database()
    {
        var probe = new EfDatabaseProbe(() => NewContext("fresh.db"));

        DatabaseState state = await probe.InspectAsync(CancellationToken.None);

        Assert.True(state.CanConnect);
        Assert.NotEmpty(state.PendingMigrations);
    }

    [Fact]
    public async Task database_probe_sees_a_migrated_database_as_up_to_date()
    {
        await using (WebDevLoopDbContext context = NewContext("migrated.db"))
        {
            await PersistenceDatabase.MigrateAsync(context, CancellationToken.None);
        }

        DatabaseState state = await new EfDatabaseProbe(() => NewContext("migrated.db")).InspectAsync(CancellationToken.None);

        Assert.True(state.CanConnect);
        Assert.Empty(state.PendingMigrations);
    }

    [Fact]
    public async Task database_probe_reports_an_unopenable_database_as_unreachable()
    {
        var probe = new EfDatabaseProbe(() => NewContext(Path.Combine("missing-directory", "app.db")));

        DatabaseState state = await probe.InspectAsync(CancellationToken.None);

        Assert.False(state.CanConnect);
    }

    [Fact]
    public void libgit2_probe_loads_the_native_library()
    {
        string version = new LibGit2NativeProbe().LoadNativeLibrary();

        Assert.False(string.IsNullOrWhiteSpace(version));
    }

    private WebDevLoopDbContext NewContext(string relativePath) =>
        new(new DbContextOptionsBuilder<WebDevLoopDbContext>()
            .UseSqlite($"Data Source={Path.Combine(_directory.Path, relativePath)};Pooling=False")
            .Options);
}
