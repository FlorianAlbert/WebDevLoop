using System.Globalization;
using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Orchestration.Preparation;
using WebDevLoop.Core.Orchestration.TicketExecution;
using WebDevLoop.Core.Settings;

namespace WebDevLoop.Core.Orchestration.ReviewLoop;

/// <summary>Placeholder values for one reviewer turn (spec, ticket, checkout, and review context).</summary>
internal static class ReviewerPromptValues
{
    /// <summary>Rendered for ticket placeholders in a parent-spec review, which has no ticket.</summary>
    private const string NotApplicable = "n/a";
    private const string NoChangedFiles = "(no changed files)";

    public static IReadOnlyDictionary<string, string> Build(
        ReviewContext context,
        FindingAxis axis,
        int attempt,
        string skillsRoot,
        IReadOnlyList<TicketRun> tickets,
        IReadOnlyList<TicketDependency> dependencies)
    {
        (ReviewRequest request, SpecRun spec, TicketRun? ticket, RepositoryRecord repository, EffectiveSettings settings) = context;
        Dictionary<string, string> values = SpecPromptValues.ForSpec(
            spec, repository, settings, RunWorkspaceLayout.For(settings.WorkspaceRootDirectory, spec.Id), attempt,
            context.IntegrationTip, skillsRoot, tickets, dependencies);
        if (ticket is null)
        {
            AddNotApplicableTicket(values);
        }
        else
        {
            SpecPromptValues.AddTicket(values, ticket, tickets, dependencies);
        }

        ReviewTarget target = request.Target;
        values[PromptPlaceholders.WorktreePath] = target.WorkingDirectory;
        values[PromptPlaceholders.BranchName] = target.Branch.Value;
        values[PromptPlaceholders.ReviewAxis] = ReviewJson.Name(axis);
        values[PromptPlaceholders.ReviewScope] = ReviewJson.Name(request.Scope);
        values[PromptPlaceholders.ReviewIteration] = request.Round.Iteration.ToString(CultureInfo.InvariantCulture);
        values[PromptPlaceholders.MaxReviewIterations] = settings.MaxReviewIterations.ToString(CultureInfo.InvariantCulture);
        values[PromptPlaceholders.DiffBaseRef] = target.DiffBase.Value;
        values[PromptPlaceholders.DiffHeadRef] = target.DiffHead.Value;
        values[PromptPlaceholders.ChangedFiles] = context.ChangedFiles.Count == 0 ? NoChangedFiles : string.Join('\n', context.ChangedFiles);
        return values;
    }

    private static void AddNotApplicableTicket(Dictionary<string, string> values)
    {
        values[PromptPlaceholders.TicketIssueNumber] = NotApplicable;
        values[PromptPlaceholders.TicketTitle] = NotApplicable;
        values[PromptPlaceholders.TicketBody] = NotApplicable;
        values[PromptPlaceholders.TicketDependencies] = NotApplicable;
    }
}
