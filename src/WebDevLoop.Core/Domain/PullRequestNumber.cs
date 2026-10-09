namespace WebDevLoop.Core.Domain;

public readonly record struct PullRequestNumber
{
    public PullRequestNumber(int value)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(value);
        Value = value;
    }

    public int Value { get; }

    public override string ToString() => Value.ToString();
}
