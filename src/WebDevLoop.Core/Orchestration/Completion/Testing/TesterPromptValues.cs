using System.Globalization;
using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Orchestration.Preparation;
using WebDevLoop.Core.Orchestration.TicketExecution;
using WebDevLoop.Core.Ports;
using WebDevLoop.Core.Settings;

namespace WebDevLoop.Core.Orchestration.Completion.Testing;

/// <summary>Placeholder values for one tester turn (spec, test checkout, run instructions, reserved port and URL).</summary>
internal static class TesterPromptValues
{
    public static IReadOnlyDictionary<string, string> Build(TesterContext context, int attempt, TestTarget target, string skillsRoot)
    {
        Dictionary<string, string> values = SpecPromptValues.ForSpec(
            context.Spec,
            context.Repository,
            context.Settings,
            RunWorkspaceLayout.For(context.Settings.WorkspaceRootDirectory, context.Spec.Id),
            attempt,
            context.Head,
            skillsRoot,
            context.Tickets,
            context.Dependencies);
        values[PromptPlaceholders.WorktreePath] = context.Workspace.CheckoutDirectory;
        values[PromptPlaceholders.BranchName] = context.Workspace.Branch.Value;
        values[PromptPlaceholders.TesterInstructions] = context.Settings.TesterRunInstructions;
        values[PromptPlaceholders.ReservedPort] = target.Port.ToString(CultureInfo.InvariantCulture);
        values[PromptPlaceholders.AppUrl] = target.AppUrl.ToString();
        return values;
    }
}
