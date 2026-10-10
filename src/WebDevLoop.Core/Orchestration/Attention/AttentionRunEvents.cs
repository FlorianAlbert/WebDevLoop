using System.Text.Json;
using WebDevLoop.Core.Domain;

namespace WebDevLoop.Core.Orchestration.Attention;

/// <summary>The run events that record what the resolution pipeline did, shown on the run page timeline.</summary>
public static class AttentionRunEvents
{
    /// <summary>Something needs attention; the summary is the one shown on the "Action needed" card.</summary>
    public const string Raised = "AttentionRaised";

    /// <summary>A remediation ran; counted to bound it.</summary>
    public const string RemediationAttempted = "AttentionRemediationAttempted";

    /// <summary>WebDevLoop resolved the situation itself and resumed the work.</summary>
    public const string AutoResolved = "AttentionAutoResolved";

    /// <summary>Remediation did not resolve the situation; the user is asked.</summary>
    public const string NeedsYou = "AttentionNeedsYou";

    public static RunEvent Raise(AttentionCase attentionCase, DateTimeOffset at) => Create(
        attentionCase, Raised, new { code = attentionCase.Reason.Code.ToString(), summary = attentionCase.Reason.Summary, cause = attentionCase.Reason.Cause.ToString() }, at);

    public static RunEvent Attempt(AttentionCase attentionCase, string stage, AttentionStageResult result, DateTimeOffset at) => Create(
        attentionCase,
        RemediationAttempted,
        new { code = attentionCase.Reason.Code.ToString(), stage, outcome = result.Status.ToString(), summary = result.Summary },
        at);

    public static RunEvent Resolve(AttentionCase attentionCase, string stage, AttentionStageResult result, DateTimeOffset at) => Create(
        attentionCase,
        AutoResolved,
        new
        {
            code = attentionCase.Reason.Code.ToString(),
            stage,
            summary = result.Summary,
            resume = result.Resume?.Kind.ToString(),
        },
        at);

    public static RunEvent AskUser(AttentionCase attentionCase, IReadOnlyList<string> tried, DateTimeOffset at) => Create(
        attentionCase,
        NeedsYou,
        new { code = attentionCase.Reason.Code.ToString(), summary = attentionCase.Reason.Summary, tried },
        at);

    /// <summary>How often <paramref name="stage"/> ran for the case's owner and code since the user's last Retry.</summary>
    public static int CountAttempts(IEnumerable<RunEvent> events, AttentionCase attentionCase, string stage)
    {
        RunEvent[] own = events
            .Where(runEvent => runEvent.TicketRunId == attentionCase.TicketRunId)
            .OrderBy(runEvent => runEvent.OccurredAt)
            .ThenBy(runEvent => runEvent.Id)
            .ToArray();
        int lastUserRetry = Array.FindLastIndex(own, runEvent => runEvent.Type == "ControlRetry");
        string code = attentionCase.Reason.Code.ToString();
        return own.Skip(lastUserRetry + 1).Count(runEvent => runEvent.Type == RemediationAttempted && IsAttempt(runEvent, code, stage));
    }

    private static bool IsAttempt(RunEvent runEvent, string code, string stage)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(runEvent.PayloadJson);
            JsonElement root = document.RootElement;
            return root.TryGetProperty("code", out JsonElement c) && c.GetString() == code
                && root.TryGetProperty("stage", out JsonElement s) && s.GetString() == stage;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static RunEvent Create(AttentionCase attentionCase, string type, object payload, DateTimeOffset at) =>
        RunEvent.Create(attentionCase.SpecRunId, attentionCase.TicketRunId, type, JsonSerializer.Serialize(payload), at);
}
