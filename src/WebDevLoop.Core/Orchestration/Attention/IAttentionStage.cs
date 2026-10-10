namespace WebDevLoop.Core.Orchestration.Attention;

/// <summary>
/// One step of the resolution order for a run or ticket that needs attention: known automatic remediation first (see
/// <see cref="KnownRemediationStage"/>), then, when registered, a troubleshooter agent session, and only then the user.
/// Stages run in registration order; the first one that returns <see cref="AttentionStageStatus.Resolved"/> ends the pipeline.
/// A stage verifies its own success (it re-runs the check that failed) and is bounded, so the pipeline cannot loop.
/// </summary>
public interface IAttentionStage
{
    /// <summary>Names the stage in the run history, e.g. <c>Remediation</c>.</summary>
    string Name { get; }

    Task<AttentionStageResult> TryAsync(AttentionCase attentionCase, CancellationToken cancellationToken);
}
