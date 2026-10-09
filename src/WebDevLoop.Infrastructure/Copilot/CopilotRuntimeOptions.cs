namespace WebDevLoop.Infrastructure.Copilot;

public sealed class CopilotRuntimeOptions
{
    /// <summary>Copilot home (session state, config) shared by every runtime, so sessions resume after a runtime is replaced.</summary>
    public required string BaseDirectory { get; init; }

    /// <summary>Copilot CLI to launch; null uses the runtime bundled into the app output by the SDK package.</summary>
    public string? CliPath { get; init; }

    /// <summary>Runtimes whose App installation token expires within this window are drained and replaced.</summary>
    public TimeSpan TokenRefreshSkew { get; init; } = TimeSpan.FromMinutes(5);

    /// <summary>Runtimes no session has leased for this long are stopped by <c>EvictIdleAsync</c>.</summary>
    public TimeSpan IdleTimeout { get; init; } = TimeSpan.FromMinutes(10);

    /// <summary>Environment the agent shell environment is derived from (scrubbed by the role policy); defaults to this process.</summary>
    public Func<IReadOnlyDictionary<string, string>> InheritedEnvironment { get; init; } = ReadProcessEnvironment;

    private static IReadOnlyDictionary<string, string> ReadProcessEnvironment() =>
        Environment.GetEnvironmentVariables()
            .Cast<System.Collections.DictionaryEntry>()
            .ToDictionary(entry => (string)entry.Key, entry => entry.Value?.ToString() ?? string.Empty, StringComparer.Ordinal);
}
