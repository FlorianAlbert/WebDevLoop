using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Ports;
using WebDevLoop.Infrastructure.GitHub.Auth;
using WebDevLoop.Infrastructure.Prerequisites;

namespace WebDevLoop.Infrastructure.Tests.Prerequisites;

internal sealed class FakeProcessProbe : IProcessProbe
{
    private readonly Dictionary<string, ProcessProbeResult> _results = new(StringComparer.Ordinal);

    public List<string> Invocations { get; } = [];

    public FakeProcessProbe Responds(string executable, string arguments, ProcessProbeResult result)
    {
        _results[Key(executable, arguments)] = result;
        return this;
    }

    public Task<ProcessProbeResult> RunAsync(string executable, IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        string key = Key(executable, string.Join(' ', arguments));
        Invocations.Add(key);
        return Task.FromResult(_results.GetValueOrDefault(key, ProcessProbeResult.NotFound));
    }

    private static string Key(string executable, string arguments) => $"{executable} {arguments}";
}

internal sealed class FakeFileSystemProbe : IFileSystemProbe
{
    public HashSet<string> Files { get; } = new(StringComparer.Ordinal);

    public Dictionary<string, string> UnwritableDirectories { get; } = new(StringComparer.Ordinal);

    public List<string> WritableChecks { get; } = [];

    public bool FileExists(string path) => Files.Contains(path);

    public string? TryEnsureWritableDirectory(string directory)
    {
        WritableChecks.Add(directory);
        return UnwritableDirectories.GetValueOrDefault(directory);
    }
}

internal sealed class FakeDatabaseProbe(Func<DatabaseState> state) : IDatabaseProbe
{
    public Task<DatabaseState> InspectAsync(CancellationToken cancellationToken) => Task.FromResult(state());
}

internal sealed class FakeLibGit2Probe(Func<string> load) : ILibGit2Probe
{
    public string LoadNativeLibrary() => load();
}

internal sealed class StubCheck(string name, Func<CancellationToken, Task<PrerequisiteCheck>> run) : IPrerequisiteCheck
{
    public string Name => name;

    public Task<PrerequisiteCheck> RunAsync(CancellationToken cancellationToken) => run(cancellationToken);

    public static StubCheck Returning(string name, PrerequisiteStatus status) =>
        new(name, _ => Task.FromResult(new PrerequisiteCheck(name, status, $"{name} {status}")));
}

internal sealed class FixedClock(DateTimeOffset now) : IClock
{
    public DateTimeOffset UtcNow => now;
}

internal static class TestPrerequisiteOptions
{
    public const string WorkspaceRoot = "/work/space";
    public const string BundledCopilotCli = "/app/runtimes/linux-x64/native/copilot";

    public static PrerequisiteOptions Create(
        GitHubAuthOptions? gitHubAuth = null,
        string? copilotCliPath = null,
        TestPortRange? portRange = null,
        GhStackMode ghStackMode = GhStackMode.RestWithOptionalFallback) => new()
        {
            WorkspaceRoot = WorkspaceRoot,
            GitHubAuth = gitHubAuth ?? new GitHubAuthOptions { AppClientId = "Iv1.abc", AppPrivateKeyPem = "pem" },
            CopilotCliPath = copilotCliPath,
            BundledCopilotCliPath = BundledCopilotCli,
            TestPortRange = portRange ?? new TestPortRange(41000, 41099),
            GhStackMode = ghStackMode,
        };

    public static ProcessProbeResult Ok(string output = "1.0.0") => new(ProcessProbeOutcome.Completed, 0, output);
}
