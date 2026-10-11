using System.Text.Json;
using System.Text.Json.Serialization;
using WebDevLoop.Core.Domain;

namespace WebDevLoop.Core.Orchestration.Attention;

/// <summary>The run events of the troubleshooter stage: how often it ran (its bound), in which state (so it never loops) and what it found.</summary>
public static class TroubleshooterRunEvents
{
    public const string StageName = "Troubleshooter";

    /// <summary>A session was started for a reason code in a git state; recorded before the session so a crash cannot hide the attempt.</summary>
    public const string Started = "AttentionTroubleshooterStarted";

    /// <summary>A session ended; carries the diagnosis and whether WebDevLoop could verify a claimed repair.</summary>
    public const string Finished = "AttentionTroubleshooterFinished";

    /// <summary>A finished session whose confirmed Model loss consumes neither an attempt nor a state fingerprint.</summary>
    public const string ModelUnavailableOutcome = nameof(AttentionCode.ModelUnavailable);

    private const string StepProperty = "step";
    private const string OutcomeProperty = "outcome";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static RunEvent Start(AttentionCase attentionCase, string fingerprint, StepRunId step, int attempt, DateTimeOffset at) => Create(
        attentionCase, Started, new StartedPayload(attentionCase.Reason.Code.ToString(), fingerprint, step.Value, attempt), at);

    public static RunEvent Finish(
        AttentionCase attentionCase,
        string fingerprint,
        StepRunId step,
        string outcome,
        bool verified,
        string summary,
        AttentionStageDiagnosis? diagnosis,
        DateTimeOffset at) => Create(
        attentionCase,
        Finished,
        new FinishedPayload(
            attentionCase.Reason.Code.ToString(),
            fingerprint,
            step.Value,
            outcome,
            verified,
            summary,
            diagnosis is null ? null : new TroubleshooterFinding(
                diagnosis.Diagnosis.Summary, diagnosis.Diagnosis.ActionsTaken, diagnosis.UserSteps, [.. diagnosis.SuggestedButtons.Select(button => button.ToString())])),
        at);

    /// <summary>Sessions started for the case's owner and code since the user's last Retry, excluding confirmed Model loss.</summary>
    public static int CountStarted(IEnumerable<RunEvent> events, AttentionCase attentionCase)
    {
        RunEvent[] own = Own(events, attentionCase);
        int lastUserRetry = Array.FindLastIndex(own, runEvent => runEvent.Type == "ControlRetry");
        string code = attentionCase.Reason.Code.ToString();
        return ConsumingAttempts(own, startIndex: lastUserRetry + 1).Count(runEvent => Text(runEvent, "code") == code);
    }

    /// <summary>Whether a session was already started for this owner, code and git state, excluding confirmed Model loss.</summary>
    public static bool WasEscalated(IEnumerable<RunEvent> events, AttentionCase attentionCase, string fingerprint)
    {
        string code = attentionCase.Reason.Code.ToString();
        return ConsumingAttempts(Own(events, attentionCase)).Any(runEvent => Text(runEvent, "code") == code && Text(runEvent, "fingerprint") == fingerprint);
    }

    private static IEnumerable<RunEvent> ConsumingAttempts(RunEvent[] own, int startIndex = 0)
    {
        HashSet<string> unavailable = own
            .Where(runEvent => runEvent.Type == Finished && Text(runEvent, OutcomeProperty) == ModelUnavailableOutcome)
            .Select(runEvent => Text(runEvent, StepProperty))
            .OfType<string>()
            .ToHashSet();
        // Without a step id, Model loss cannot be confirmed for that start; retain its budget charge.
        return own.Skip(startIndex).Where(runEvent => runEvent.Type == Started
            && (Text(runEvent, StepProperty) is not { } step || !unavailable.Contains(step)));
    }

    /// <summary>The newest diagnosis a session left for this owner, code and git state, if it finished.</summary>
    public static TroubleshooterFinding? FindDiagnosis(IEnumerable<RunEvent> events, AttentionCase attentionCase, string fingerprint)
    {
        string code = attentionCase.Reason.Code.ToString();
        foreach (RunEvent runEvent in Own(events, attentionCase).Reverse().Where(runEvent => runEvent.Type == Finished && Text(runEvent, "code") == code && Text(runEvent, "fingerprint") == fingerprint))
        {
            try
            {
                using JsonDocument document = JsonDocument.Parse(runEvent.PayloadJson);
                if (document.RootElement.TryGetProperty("diagnosis", out JsonElement element) && element.ValueKind == JsonValueKind.Object)
                {
                    return element.Deserialize<TroubleshooterFinding>(Json);
                }
            }
            catch (JsonException)
            {
                // A malformed event is skipped; the diagnosis is only a convenience for the card.
            }
        }

        return null;
    }

    private static RunEvent[] Own(IEnumerable<RunEvent> events, AttentionCase attentionCase) => events
        .Where(runEvent => runEvent.TicketRunId == attentionCase.TicketRunId)
        .OrderBy(runEvent => runEvent.OccurredAt)
        .ThenBy(runEvent => runEvent.Id)
        .ToArray();

    private static string? Text(RunEvent runEvent, string property)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(runEvent.PayloadJson);
            return document.RootElement.TryGetProperty(property, out JsonElement value) ? value.GetString() : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static RunEvent Create(AttentionCase attentionCase, string type, object payload, DateTimeOffset at) =>
        RunEvent.Create(attentionCase.SpecRunId, attentionCase.TicketRunId, type, JsonSerializer.Serialize(payload), at);

    private sealed record StartedPayload(
        [property: JsonPropertyName("code")] string Code,
        [property: JsonPropertyName("fingerprint")] string Fingerprint,
        [property: JsonPropertyName(StepProperty)] string Step,
        [property: JsonPropertyName("attempt")] int Attempt);

    private sealed record FinishedPayload(
        [property: JsonPropertyName("code")] string Code,
        [property: JsonPropertyName("fingerprint")] string Fingerprint,
        [property: JsonPropertyName(StepProperty)] string Step,
        [property: JsonPropertyName(OutcomeProperty)] string Outcome,
        [property: JsonPropertyName("verified")] bool Verified,
        [property: JsonPropertyName("summary")] string Summary,
        [property: JsonPropertyName("diagnosis")] TroubleshooterFinding? Diagnosis);
}

/// <summary>The part of a finished session that is kept in its run event, to show the same diagnosis again for an unchanged state.</summary>
public sealed record TroubleshooterFinding(string Summary, IReadOnlyList<string> ActionsTaken, IReadOnlyList<string> UserSteps, IReadOnlyList<string> SuggestedButtons)
{
    public AttentionStageDiagnosis ToDiagnosis()
    {
        var buttons = new List<AttentionActionKind>();
        foreach (string button in SuggestedButtons)
        {
            if (Enum.TryParse(button, out AttentionActionKind kind))
            {
                buttons.Add(kind);
            }
        }

        return new AttentionStageDiagnosis(new AttentionDiagnosis(TroubleshooterRunEvents.StageName, Summary, ActionsTaken), UserSteps, buttons);
    }
}
