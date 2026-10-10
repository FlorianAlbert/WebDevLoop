using System.Text;
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
