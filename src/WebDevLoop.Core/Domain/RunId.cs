namespace WebDevLoop.Core.Domain;

public readonly record struct RunId
{
    public RunId(string value)
    {
        Value = BranchSegment.Require(value, nameof(value));
    }

    public string Value { get; }

    public override string ToString() => Value;
}
