using WebDevLoop.Core.Domain;

namespace WebDevLoop.Core.Orchestration;

/// <summary>Concludes an agent step with structured attention when supplied, otherwise with its ordinary outcome.</summary>
internal static class StepRunConclusion
{
    public static void Finish(
        StepRun step,
        StepStatus status,
        AttentionReason? attention,
        string? resultJson,
        string? failure,
        DateTimeOffset at)
    {
        if (attention is not null)
        {
            step.MarkNeedsAttention(attention, at, resultJson);
        }
        else
        {
            step.Finish(status, at, resultJson, failure);
        }
    }
}
