namespace WebDevLoop.Core.Orchestration.Attention;

/// <param name="TransientBackoff">The pauses before each automatic retry of a temporary GitHub or network failure; its length bounds the retries.</param>
/// <param name="Delay">Waits for a pause; replaced in tests.</param>
public sealed record AttentionTriageOptions(IReadOnlyList<TimeSpan> TransientBackoff, Func<TimeSpan, CancellationToken, Task> Delay)
{
    public static AttentionTriageOptions Default { get; } = new(
        [TimeSpan.FromSeconds(30), TimeSpan.FromMinutes(2), TimeSpan.FromMinutes(5)],
        (pause, cancellationToken) => Task.Delay(pause, cancellationToken));
}
