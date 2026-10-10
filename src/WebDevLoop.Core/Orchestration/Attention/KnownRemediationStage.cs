using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Ports;

namespace WebDevLoop.Core.Orchestration.Attention;

/// <summary>
/// Step one of the resolution order: the deterministic fix registered for the case's reason code (see
/// <see cref="IKnownRemediation"/>). Every run of a remediation is recorded as a run event, which is also how its attempts are
/// bounded: a remediation that already ran as often as it may since the user's last Retry is not run again.
/// </summary>
public sealed class KnownRemediationStage(IEnumerable<IKnownRemediation> remediations, IRunEventRepository runEvents, IClock clock) : IAttentionStage
{
    public const string StageName = "Remediation";

    private readonly Dictionary<AttentionCode, IKnownRemediation> _byCode = remediations.ToDictionary(remediation => remediation.Code);

    public string Name => StageName;

    public async Task<AttentionStageResult> TryAsync(AttentionCase attentionCase, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(attentionCase);
        if (!_byCode.TryGetValue(attentionCase.Reason.Code, out IKnownRemediation? remediation))
        {
            return AttentionStageResult.NotApplicable;
        }

        int previous = AttentionRunEvents.CountAttempts(await runEvents.ListBySpecRunAsync(attentionCase.SpecRunId, cancellationToken), attentionCase, StageName);
        if (previous >= remediation.MaxAttempts)
        {
            return AttentionStageResult.Unresolved(
                $"The automatic fix already ran {previous} time(s) without lasting success.",
                $"Automatic fix ({attentionCase.Reason.AutoFix?.Description ?? attentionCase.Reason.Code.ToString()}) ran {previous} time(s) and the problem came back.");
        }

        AttentionStageResult result;
        try
        {
            result = await remediation.TryAsync(attentionCase, previous, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            result = AttentionStageResult.Unresolved($"The automatic fix failed: {exception.Message}", $"Automatic fix failed: {exception.Message}");
        }

        runEvents.Add(AttentionRunEvents.Attempt(attentionCase, StageName, result, clock.UtcNow));
        return result;
    }
}
