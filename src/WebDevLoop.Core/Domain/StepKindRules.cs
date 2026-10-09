namespace WebDevLoop.Core.Domain;

public static class StepKindRules
{
    /// <summary>Implement and fix steps share one active slot per ticket.</summary>
    public static bool IsImplementOrFix(this StepKind kind) => kind is StepKind.Implement or StepKind.Fix;
}
