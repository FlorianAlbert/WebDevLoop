using System.Text;
using System.Text.Json;
using WebDevLoop.Core.Domain;

namespace WebDevLoop.Web.Components.Shared;

/// <summary>User-facing names for domain enums and raw identifiers.</summary>
public static class DisplayNames
{
    public static string For(SpecDependencyMode mode) => mode switch
    {
        SpecDependencyMode.WaitForMerge => "Wait for merge",
        SpecDependencyMode.StackOnTop => "Stack on top",
        _ => Humanize(mode.ToString()),
    };

    public static string Describe(SpecDependencyMode mode) => mode switch
    {
        SpecDependencyMode.WaitForMerge => "Start dependent specs only after the blocking pull request has been merged.",
        SpecDependencyMode.StackOnTop => "Start dependent specs right away, stacked on top of the blocking branch.",
        _ => string.Empty,
    };

    public static string For(AgentRole role) => role switch
    {
        AgentRole.ReviewerCodingStandards => "Reviewer – coding standards",
        AgentRole.ReviewerSpecification => "Reviewer – specification",
        AgentRole.ConflictResolver => "Conflict resolver",
        _ => Humanize(role.ToString()),
    };

    /// <summary>One-line description of a run event for the timeline; unknown types fall back to the raw type name.</summary>
    public static string DescribeRunEvent(string type, string payloadJson)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(payloadJson);
        }
        catch (JsonException)
        {
            return type;
        }

        using (document)
        {
            JsonElement root = document.RootElement;
            string? to = Text(root, "to");
            return (type, to) switch
            {
                ("SpecRunStatusChanged", { } status) => $"Run is now {Humanize(status).ToLowerInvariant()}",
                ("TicketRunStatusChanged", { } status) => $"Ticket is now {Humanize(status).ToLowerInvariant()}",
                ("StepRunStatusChanged", { } status) => $"Step is now {Humanize(status).ToLowerInvariant()}",
                ("SagaCheckpointAdvanced", { } checkpoint) => $"Integration reached {Humanize(checkpoint).ToLowerInvariant()}",
                _ => DescribeAttentionEvent(type, root) ?? type,
            };
        }
    }

    /// <summary>The events recorded by the attention pipeline and the control commands, in words a user understands; null for other types.</summary>
    private static string? DescribeAttentionEvent(string type, JsonElement payload)
    {
        string? summary = Text(payload, "summary");
        return type switch
        {
            "AttentionRaised" => $"Needs attention: {Sentence(summary) ?? "something stopped the work"}",
            "AttentionRemediationAttempted" => Text(payload, "outcome") == "Resolved"
                ? $"WebDevLoop tried an automatic fix: {Sentence(summary) ?? "it worked"}"
                : $"WebDevLoop tried an automatic fix, but it did not solve it{(Sentence(summary) is { } why ? $": {why}" : "")}",
            "AttentionAutoResolved" => $"WebDevLoop fixed it by itself: {Sentence(summary) ?? "no action needed"}{Text(payload, "resume") switch
            {
                "Retry" => " and resumed the work",
                "SkipTicket" => " and skipped the ticket",
                _ => string.Empty,
            }}",
            "AttentionNeedsYou" => $"WebDevLoop needs you: {Sentence(summary) ?? "it cannot continue on its own"}{Count(payload, "tried") switch
            {
                0 => string.Empty,
                1 => " (it already tried 1 thing)",
                var tried => $" (it already tried {tried} things)",
            }}",
            "AttentionTroubleshooterStarted" => "The Troubleshooter agent started looking into it",
            "AttentionTroubleshooterFinished" => Text(payload, "outcome") switch
            {
                "Resolved" => Sentence(summary) ?? "The Troubleshooter agent fixed it and WebDevLoop verified the result",
                "ClaimRejected" => "The Troubleshooter agent said it was fixed, but WebDevLoop's own check disagreed",
                "Failed" => "The Troubleshooter agent session failed",
                "TimedOut" => "The Troubleshooter agent ran out of time",
                "Cancelled" => "The Troubleshooter agent session was cancelled",
                _ => $"The Troubleshooter agent looked into it: {Sentence(summary) ?? "no diagnosis"}",
            },
            "ControlRetry" => "You retried it",
            "ControlAutoRetry" => "WebDevLoop retried it automatically",
            "ControlSkip" => $"You skipped the ticket{Dependents(payload)}",
            "ControlAutoSkip" => $"WebDevLoop skipped the ticket automatically{Dependents(payload)}",
            "ControlAbort" => $"You aborted it{Dependents(payload)}",
            _ => null,
        };
    }

    private static string? Text(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(property, out JsonElement value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static int Count(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(property, out JsonElement value) && value.ValueKind == JsonValueKind.Array ? value.GetArrayLength() : 0;

    private static string Dependents(JsonElement payload) => Count(payload, "tickets") switch
    {
        0 => string.Empty,
        1 => " and 1 ticket that depended on it",
        var count => $" and {count} tickets that depended on it",
    };

    private static string? Sentence(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim().TrimEnd('.');

    /// <summary>Splits PascalCase into sentence case: "NeedsAttention" becomes "Needs attention".</summary>
    public static string Humanize(string? identifier)
    {
        if (string.IsNullOrEmpty(identifier))
        {
            return string.Empty;
        }

        StringBuilder builder = new(identifier.Length + 4);
        for (int i = 0; i < identifier.Length; i++)
        {
            char c = identifier[i];
            if (i == 0)
            {
                builder.Append(char.ToUpperInvariant(c));
            }
            else if (char.IsUpper(c) && !char.IsUpper(identifier[i - 1]))
            {
                builder.Append(' ').Append(char.ToLowerInvariant(c));
            }
            else
            {
                builder.Append(c);
            }
        }

        return builder.ToString();
    }
}
