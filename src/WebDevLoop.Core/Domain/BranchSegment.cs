namespace WebDevLoop.Core.Domain;

/// <summary>Validation for identifiers that are embedded as one segment of a Git branch name.</summary>
internal static class BranchSegment
{
    public static string Require(string? value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Value must not be blank.", parameterName);
        }

        if (value.Contains("..", StringComparison.Ordinal) || !value.All(IsAllowed))
        {
            throw new ArgumentException($"'{value}' is not a valid branch name segment.", parameterName);
        }

        return value;
    }

    private static bool IsAllowed(char c) => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.';
}
