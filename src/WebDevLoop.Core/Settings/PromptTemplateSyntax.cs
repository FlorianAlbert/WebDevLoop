using System.Text.RegularExpressions;

namespace WebDevLoop.Core.Settings;

/// <summary>Placeholder syntax shared by validation and rendering: <c>{lower_snake_case}</c>.</summary>
internal static partial class PromptTemplateSyntax
{
    [GeneratedRegex(@"\{(?<name>[a-z][a-z0-9_]*)\}", RegexOptions.CultureInvariant)]
    public static partial Regex Placeholder();

    public static IEnumerable<string> PlaceholderNames(string template) =>
        Placeholder().Matches(template).Select(match => match.Groups["name"].Value).Distinct(StringComparer.Ordinal);
}
