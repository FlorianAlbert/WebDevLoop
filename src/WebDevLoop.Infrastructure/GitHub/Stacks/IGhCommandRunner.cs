namespace WebDevLoop.Infrastructure.GitHub.Stacks;

public sealed record GhCommandResult(int ExitCode, string StandardOutput, string StandardError);

/// <summary>Runs the <c>gh</c> CLI non-interactively; used only as fallback when the stack REST API is unavailable.</summary>
public interface IGhCommandRunner
{
    Task<GhCommandResult> RunAsync(
        IReadOnlyList<string> arguments,
        IReadOnlyDictionary<string, string> environment,
        CancellationToken cancellationToken);
}
