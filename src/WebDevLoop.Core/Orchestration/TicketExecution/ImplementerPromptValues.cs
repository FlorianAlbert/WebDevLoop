using System.Globalization;
using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Settings;

namespace WebDevLoop.Core.Orchestration.TicketExecution;

/// <summary>Placeholder values for an implementer turn: spec, ticket, and worktree context plus the findings to fix.</summary>
internal static class ImplementerPromptValues
{
    private const string NoReviewFindingsJson = "[]";
    private const int InitialReviewIteration = 0;

    /// <param name="reviewFindingsJson">Findings of a fix turn; null for the initial implementation.</param>
    /// <param name="reviewIteration">Review rounds completed so far (0 for the initial implementation).</param>
    public static IReadOnlyDictionary<string, string> Build(
        ImplementationContext context,
        int attempt,
        CommitSha integrationTip,
        string skillsRoot,
        IReadOnlyList<TicketRun> tickets,
        IReadOnlyList<TicketDependency> dependencies,
        string? reviewFindingsJson = null,
        int reviewIteration = InitialReviewIteration)
    {
        (SpecRun spec, TicketRun ticket, RepositoryRecord repository, EffectiveSettings settings, _, _) = context;
        Dictionary<string, string> values = SpecPromptValues.ForSpec(
            spec, repository, settings, context.Layout, attempt, integrationTip, skillsRoot, tickets, dependencies);
        SpecPromptValues.AddTicket(values, ticket, tickets, dependencies);
        values[PromptPlaceholders.WorktreePath] = context.WorktreePath;
        values[PromptPlaceholders.BranchName] = ticket.BranchName.Value;
        values[PromptPlaceholders.ReviewFindingsJson] = reviewFindingsJson ?? NoReviewFindingsJson;
        values[PromptPlaceholders.ReviewIteration] = reviewIteration.ToString(CultureInfo.InvariantCulture);
        values[PromptPlaceholders.MaxReviewIterations] = settings.MaxReviewIterations.ToString(CultureInfo.InvariantCulture);
        return values;
    }
}
