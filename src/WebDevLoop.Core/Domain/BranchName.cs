namespace WebDevLoop.Core.Domain;

public readonly record struct BranchName
{
    public BranchName(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Branch name must not be blank.", nameof(value));
        }

        if (value.Split('/').Any(IsInvalidSegment) || value.Contains("..", StringComparison.Ordinal) || value.Any(char.IsWhiteSpace))
        {
            throw new ArgumentException($"'{value}' is not a valid branch name.", nameof(value));
        }

        Value = value;
    }

    public string Value { get; }

    public override string ToString() => Value;

    private static bool IsInvalidSegment(string segment) => segment.Length == 0;
}
