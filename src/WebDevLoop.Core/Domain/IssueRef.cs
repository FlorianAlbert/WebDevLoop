namespace WebDevLoop.Core.Domain;

public readonly record struct IssueRef
{
    public IssueRef(string owner, string repo, int number, string? nodeId = null, long? databaseId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(owner);
        ArgumentException.ThrowIfNullOrWhiteSpace(repo);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(number);
        Owner = owner;
        Repo = repo;
        Number = number;
        NodeId = nodeId;
        DatabaseId = databaseId;
    }

    public string Owner { get; }

    public string Repo { get; }

    public int Number { get; }

    public string? NodeId { get; }

    public long? DatabaseId { get; }

    public override string ToString() => $"{Owner}/{Repo}#{Number}";
}
