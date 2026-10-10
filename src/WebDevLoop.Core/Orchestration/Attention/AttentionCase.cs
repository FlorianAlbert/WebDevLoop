using WebDevLoop.Core.Domain;

namespace WebDevLoop.Core.Orchestration.Attention;

/// <summary>One run or ticket parked in <c>NeedsAttention</c> that the resolution pipeline looks at.</summary>
/// <param name="TicketRunId">Null when the spec run itself needs attention.</param>
public sealed record AttentionCase(RunId SpecRunId, TicketRunId? TicketRunId, AttentionReason Reason)
{
    public bool IsTicket => TicketRunId.HasValue;
}
