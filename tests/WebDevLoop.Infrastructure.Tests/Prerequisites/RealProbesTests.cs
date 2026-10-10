using Microsoft.EntityFrameworkCore;
using WebDevLoop.Infrastructure.Persistence;
using WebDevLoop.Infrastructure.Prerequisites;

namespace WebDevLoop.Infrastructure.Tests.Prerequisites;

[Collection("Process environment")]
public sealed class RealProbesTests : IDisposable
{
    // Muxer-only invocations: `dotnet --version` and unknown commands boot the whole SDK CLI, which is slow and can spawn
    // helper processes when the solution's tests run side by side.
    private static readonly TimeSpan GenerousTimeout = TimeSpan.FromMinutes(2);
    private const string MissingApplication = "webdevloop-missing-app.dll";

    private readonly TestDirectory _directory = new("probes");

    public void Dispose() => _directory.Dispose();

    [Theory]
    [InlineData("", false)]
    [InlineData(".cmd", false)]
    [InlineData(".bat", false)]
    [InlineData("", true)]
    [InlineData(".cmd", true)]
    [InlineData(".bat", true)]
    public async Task process_probe_runs_platform_scripts_by_path_or_name(string extension, bool searchPath)
    {
        string directory = Path.Combine(_directory.Path, "directory with spaces");
        Directory.CreateDirectory(directory);
        string scriptName = "prerequisite-probe" + extension;
        string scriptPath = Path.Combine(directory, scriptName);
        if (OperatingSystem.IsWindows())
        {
            if (extension.Length == 0)
            {
                scriptPath += ".cmd";
                await File.WriteAllTextAsync(Path.Combine(directory, scriptName), "#!/bin/sh\nexit 1\n", TestContext.Current.CancellationToken);
            }

            await File.WriteAllTextAsync(scriptPath, "@echo off\r\necho %GH_PROMPT_DISABLED%/%NO_COLOR%/%~1\r\n", TestContext.Current.CancellationToken);
        }
        else
        {
            await File.WriteAllTextAsync(scriptPath, "#!/bin/sh\nprintf '%s/%s/%s\\n' \"$GH_PROMPT_DISABLED\" \"$NO_COLOR\" \"$1\"\n", TestContext.Current.CancellationToken);
            File.SetUnixFileMode(scriptPath, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

        string? originalPath = Environment.GetEnvironmentVariable("PATH");
        try
        {
            if (searchPath)
            {
                Environment.SetEnvironmentVariable("PATH", directory);
            }

            string executable = searchPath ? scriptName : Path.Combine(directory, scriptName);
            var probe = new ProcessProbe(GenerousTimeout);

            ProcessProbeResult result = await probe.RunAsync(executable, ["argument with spaces"], CancellationToken.None);

            Assert.True(result.Succeeded);
            Assert.Equal("1/1/argument with spaces", result.Output);
        }
        finally
        {
            Environment.SetEnvironmentVariable("PATH", originalPath);
        }
    }

    [Fact]
    public async Task process_probe_reports_a_missing_executable_as_not_found()
    {
        var probe = new ProcessProbe(GenerousTimeout);

        ProcessProbeResult result = await probe.RunAsync("webdevloop-no-such-tool", ["--version"], CancellationToken.None);

        Assert.Equal(ProcessProbeOutcome.NotFound, result.Outcome);
        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task process_probe_captures_exit_code_and_output()
    {
        var probe = new ProcessProbe(GenerousTimeout);

        ProcessProbeResult ok = await probe.RunAsync("dotnet", ["--list-runtimes"], CancellationToken.None);
        ProcessProbeResult failing = await probe.RunAsync("dotnet", [MissingApplication], CancellationToken.None);

        Assert.True(ok.Succeeded);
        Assert.False(string.IsNullOrWhiteSpace(ok.Output));
        Assert.Equal(ProcessProbeOutcome.Completed, failing.Outcome);
        Assert.NotEqual(0, failing.ExitCode);
    }

    [Fact]
    public async Task process_probe_times_out_and_stops_a_hanging_tool()
    {
        (string executable, string[] arguments) = HangingTool();
        var probe = new ProcessProbe(TimeSpan.FromMilliseconds(300));

        ProcessProbeResult result = await probe.RunAsync(executable, arguments, CancellationToken.None);

        Assert.Equal(ProcessProbeOutcome.TimedOut, result.Outcome);
    }

    [Fact]
    public async Task process_probe_propagates_caller_cancellation()
    {
        (string executable, string[] arguments) = HangingTool();
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));
        var probe = new ProcessProbe(GenerousTimeout);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => probe.RunAsync(executable, arguments, cts.Token));
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

    private (string Executable, string[] Arguments) HangingTool()
    {
        if (!OperatingSystem.IsWindows())
        {
            return ("sleep", ["30"]);
        }

        string script = Path.Combine(_directory.Path, "hanging-tool.cmd");
        string ping = Path.Combine(Environment.SystemDirectory, "ping.exe");
        File.WriteAllText(script, $"@echo off\r\n\"{ping}\" -n 31 127.0.0.1 >nul\r\n");
        return (script, []);
    }

    private WebDevLoopDbContext NewContext(string relativePath) =>
        new(new DbContextOptionsBuilder<WebDevLoopDbContext>()
            .UseSqlite($"Data Source={Path.Combine(_directory.Path, relativePath)};Pooling=False")
            .Options);
}

[CollectionDefinition("Process environment", DisableParallelization = true)]
public sealed class ProcessEnvironmentCollection;
