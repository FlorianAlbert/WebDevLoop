using WebDevLoop.Core.Domain;

namespace WebDevLoop.Core.Orchestration.Results;

/// <summary>
/// Troubleshooter result. <see cref="Summary"/> is the diagnosis the user reads on the "Action needed" card, and
/// <see cref="ActionsTaken"/> becomes part of "What WebDevLoop tried". <see cref="Outcome"/> <c>resolved</c> is only a claim.
/// </summary>
public sealed record TroubleshooterReport : AgentReport
{
    public TroubleshooterReport(
        TroubleshooterOutcome outcome,
        string summary,
        IReadOnlyList<string> actionsTaken,
        string verification,
        IReadOnlyList<string> userSteps,
        IReadOnlyList<AttentionActionKind> suggestedButtons)
        : base(ReportGuard.RequireText(summary, nameof(summary)))
    {
        Outcome = outcome;
        ActionsTaken = ReportGuard.RequireTextList(actionsTaken, "actions_taken");
        Verification = outcome == TroubleshooterOutcome.Resolved
            ? ReportGuard.RequireText(verification, nameof(verification))
            : verification?.Trim() ?? string.Empty;
        UserSteps = ReportGuard.RequireTextList(userSteps, "user_steps", requireAny: outcome == TroubleshooterOutcome.NeedsUser);
        SuggestedButtons = RequireButtons(suggestedButtons);
    }

    public TroubleshooterOutcome Outcome { get; }

    /// <summary>What the agent changed or ran to repair the situation, oldest first.</summary>
    public IReadOnlyList<string> ActionsTaken { get; }

    /// <summary>How the agent checked its own repair (the commands it ran and what they showed).</summary>
    public string Verification { get; }

    /// <summary>What the user has to do when the agent could not fix it, in order.</summary>
    public IReadOnlyList<string> UserSteps { get; }

    /// <summary>The buttons of the "Action needed" card that make sense after the diagnosis, most useful first.</summary>
    public IReadOnlyList<AttentionActionKind> SuggestedButtons { get; }

    private static IReadOnlyList<AttentionActionKind> RequireButtons(IReadOnlyList<AttentionActionKind>? buttons)
    {
        if (buttons is null)
        {
            throw new InvalidAgentReportException("'suggested_buttons' must be a list.");
        }

        return buttons.Distinct().Count() != buttons.Count
            ? throw new InvalidAgentReportException("'suggested_buttons' must not repeat a button.")
            : buttons;
    }
}
