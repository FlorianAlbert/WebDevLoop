using WebDevLoop.Core.Domain;

namespace WebDevLoop.Web.Tests.Components.Support;

/// <summary>Attention reasons for the UI tests: hand-built ones with every section filled, and real catalogue ones.</summary>
internal static class AttentionData
{
    public const string Command = "git -C /work/t1 status --short";

    /// <summary>Every section of the card is present: tried list, auto-fix outcome, numbered steps with a command and both link kinds, all four buttons.</summary>
    public static AttentionReason Full(AttentionCause cause = AttentionCause.You, AttentionAutoFix? autoFix = null, string[]? tried = null) => new(
        AttentionCode.WorktreeNotClean,
        "The ticket's working folder has files that were not committed.",
        "WebDevLoop cannot verify a clean checkout while they are there.",
        "fatal: /work/t1/very/long/path/that/must/wrap/0123456789abcdef0123456789abcdef0123456789abcdef is dirty",
        cause,
        autoFix,
        tried ?? ["Retried the ticket 2 times"],
        [
            new AttentionStep("Look at what is in the folder.", Command),
            new AttentionStep("Check the base branch in Settings.", null, "Open Settings", "/settings#section-general"),
            new AttentionStep("Read the GitHub docs.", null, "Open GitHub", "https://github.com/acme/widgets/branches"),
            new AttentionStep("Press Retry."),
        ],
        [
            new AttentionAction(AttentionActionKind.Retry, "Retry", "Starts the failed phase again."),
            new AttentionAction(AttentionActionKind.Skip, "Skip", "Gives up on this ticket without its change."),
            new AttentionAction(AttentionActionKind.SkipWithDependents, "Skip with dependents", "Gives up on this ticket and its dependents."),
            new AttentionAction(AttentionActionKind.Abort, "Abort ticket", "Stops this ticket for good."),
        ]);

    public static AttentionReason RunReason(AttentionCause cause = AttentionCause.Decision) => new(
        AttentionCode.ParentReviewCycleLimit,
        "The parent review still finds problems after 3 rounds.",
        "WebDevLoop stops here so it does not loop forever.",
        "review cycle 3 of 3",
        cause,
        null,
        [],
        [new AttentionStep("Read the review findings, then decide.")],
        [
            new AttentionAction(AttentionActionKind.Retry, "Retry", "Starts another review round."),
            new AttentionAction(AttentionActionKind.Abort, "Abort run", "Cancels the whole run and its unfinished tickets."),
        ]);

    public static AttentionReason Pending() => AttentionReasons.IntegrationBranchExists("integration/run-1", "ref exists") ;
}
