using System.Text.Json;
using System.Text.Json.Serialization;

namespace WebDevLoop.Core.Domain;

/// <summary>Who has to act for the item to move on.</summary>
public enum AttentionCause
{
    /// <summary>The problem is inside WebDevLoop's own working area or agents; WebDevLoop fixes it itself where it can.</summary>
    WebDevLoop,

    /// <summary>Something outside WebDevLoop (GitHub, settings, the spec) has to be fixed by the user.</summary>
    You,

    /// <summary>Nothing is broken; the user has to choose how to proceed.</summary>
    Decision,
}

/// <summary>The control commands a reason can offer; the page only renders the buttons the reason lists.</summary>
public enum AttentionActionKind
{
    Retry,
    Skip,
    SkipWithDependents,
    Abort,
}

/// <summary>One button of the "Action needed" card and what pressing it causes, in one plain sentence.</summary>
public sealed record AttentionAction(AttentionActionKind Kind, string Label, string Consequence);

/// <summary>A numbered step for the user; <see cref="Command"/> is shown in a copy-able code block and <see cref="Link"/> as a link.</summary>
public sealed record AttentionStep(string Text, string? Command = null, string? LinkLabel = null, string? LinkHref = null);

/// <summary>
/// The automatic remediation WebDevLoop attempts before asking anyone. <see cref="Attempted"/> flips once the remediation
/// pipeline ran and did not resolve the situation; <see cref="Outcome"/> then says what happened.
/// </summary>
public sealed record AttentionAutoFix(string Description, bool Attempted = false, string? Outcome = null);

/// <summary>
/// Structured guidance replacing the free-form failure string: what happened in plain language, why it matters, what
/// WebDevLoop already tried, what the user can do, and the buttons that make sense. Created where the failure is detected.
/// A run, ticket or step cannot enter <c>NeedsAttention</c> without one, which is why the constructor refuses an
/// incomplete reason.
/// </summary>
public sealed class AttentionReason
{
    [JsonConstructor]
    public AttentionReason(
        AttentionCode code,
        string summary,
        string whyItMatters,
        string details,
        AttentionCause cause,
        AttentionAutoFix? autoFix,
        IReadOnlyList<string>? triedSoFar,
        IReadOnlyList<AttentionStep>? userSteps,
        IReadOnlyList<AttentionAction>? actions)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(summary);
        ArgumentException.ThrowIfNullOrWhiteSpace(whyItMatters);
        if (!Enum.IsDefined(code))
        {
            throw new ArgumentOutOfRangeException(nameof(code), code, "An attention reason needs a known reason code.");
        }

        if (actions is null or { Count: 0 })
        {
            throw new ArgumentException("An attention reason needs at least one action.", nameof(actions));
        }

        Code = code;
        Summary = summary.Trim();
        WhyItMatters = whyItMatters.Trim();
        Details = details ?? string.Empty;
        Cause = cause;
        AutoFix = autoFix;
        TriedSoFar = triedSoFar ?? [];
        UserSteps = userSteps ?? [];
        Actions = actions;
    }

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    public AttentionCode Code { get; }

    /// <summary>One sentence in plain language: what happened.</summary>
    public string Summary { get; }

    /// <summary>One sentence: why WebDevLoop cannot simply continue.</summary>
    public string WhyItMatters { get; }

    /// <summary>The technical wording of the failure (exception text, paths, hashes), for the collapsible section.</summary>
    public string Details { get; }

    public AttentionCause Cause { get; }

    public AttentionAutoFix? AutoFix { get; }

    /// <summary>What WebDevLoop already did about it, oldest first (e.g. "Retried 2 times").</summary>
    public IReadOnlyList<string> TriedSoFar { get; }

    public IReadOnlyList<AttentionStep> UserSteps { get; }

    public IReadOnlyList<AttentionAction> Actions { get; }

    /// <summary>WebDevLoop still has an automatic remediation to run for this reason, so the page should not ask the user yet.</summary>
    [JsonIgnore]
    public bool AutoFixPending => AutoFix is { Attempted: false };

    /// <summary>The action the card highlights: the first one listed.</summary>
    [JsonIgnore]
    public AttentionAction PrimaryAction => Actions[0];

    public AttentionReason WithTried(params string[] attempts) =>
        attempts.Length == 0
            ? this
            : new AttentionReason(Code, Summary, WhyItMatters, Details, Cause, AutoFix, [.. TriedSoFar, .. attempts], UserSteps, Actions);

    public AttentionReason WithAutoFixAttempted(string outcome) =>
        new(Code, Summary, WhyItMatters, Details, Cause, (AutoFix ?? new AttentionAutoFix(outcome)) with { Attempted = true, Outcome = outcome }, TriedSoFar, UserSteps, Actions);

    public AttentionReason WithDetails(string details) =>
        new(Code, Summary, WhyItMatters, details, Cause, AutoFix, TriedSoFar, UserSteps, Actions);

    public string ToJson() => JsonSerializer.Serialize(this, JsonOptions);

    /// <returns>Null for missing, malformed or incomplete stored JSON, so one bad row never breaks a page.</returns>
    public static AttentionReason? TryParse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<AttentionReason>(json, JsonOptions);
        }
        catch (Exception exception) when (exception is JsonException or ArgumentException)
        {
            return null;
        }
    }
}
