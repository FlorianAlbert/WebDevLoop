using WebDevLoop.Core.Queries;

namespace WebDevLoop.Web.Components.Steps;

/// <summary>The model and reasoning effort a step's agent session actually ran with, as captured at launch.</summary>
public static class StepModelText
{
    public const string NotAnAgentStep = "–";
    public const string Unrecorded = "unknown";

    public static string For(StepRunView step) => (step.AgentRole, step.Model, step.ReasoningEffort) switch
    {
        (null, _, _) => NotAnAgentStep,
        (_, null, _) => Unrecorded,
        (_, { } model, null) => model,
        (_, { } model, { } effort) => $"{model} ({effort})",
    };
}
