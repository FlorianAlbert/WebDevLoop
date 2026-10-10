using WebDevLoop.Core.Domain;

namespace WebDevLoop.Web.Components.Shared;

/// <summary>Plain-language wording and link rules of the "Action needed" card.</summary>
public static class AttentionDisplay
{
    public static string CauseLabel(AttentionCause cause) => cause switch
    {
        AttentionCause.WebDevLoop => "WebDevLoop got stuck",
        AttentionCause.You => "You need to fix something",
        AttentionCause.Decision => "Your decision",
        _ => DisplayNames.Humanize(cause.ToString()),
    };

    public static StatusVariant CauseVariant(AttentionCause cause) => cause switch
    {
        AttentionCause.WebDevLoop => StatusVariant.Info,
        AttentionCause.You => StatusVariant.Warning,
        _ => StatusVariant.Neutral,
    };

    /// <summary>Relative links ("/settings#section-roles") stay in the app; everything else is external and opens in a new tab.</summary>
    public static bool IsInApp(string href) => href.StartsWith('/') && !href.StartsWith("//", StringComparison.Ordinal);

    /// <summary>The reason to show for a run, ticket or step that needs attention; rows without a structured one get the generic guidance.</summary>
    public static AttentionReason Resolve(AttentionReason? reason, string? failureReason, bool forTicket) =>
        reason ?? AttentionReasons.Unclassified(failureReason, forTicket);

    /// <summary>One line for lists and alerts: the summary, or the raw failure for rows that have no structured reason.</summary>
    public static string? Headline(AttentionReason? reason, string? failureReason) => reason?.Summary ?? failureReason;

    /// <summary>The label of the button the card highlights, e.g. "Retry".</summary>
    public static string? PrimaryActionLabel(AttentionReason? reason) => reason is { AutoFixPending: false } ? reason.PrimaryAction.Label : null;

    /// <summary>The call to action of a link to a run that needs attention: "Fix and continue" when the user has to fix something first, else the primary button.</summary>
    public static string CallToAction(AttentionReason? reason) => reason switch
    {
        null => "Open",
        { AutoFixPending: true } => "Open (WebDevLoop is on it)",
        { Cause: AttentionCause.You } => "Fix and continue",
        _ => reason.PrimaryAction.Label,
    };
}
