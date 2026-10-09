namespace WebDevLoop.Core.Domain;

public readonly record struct GitHubRepoRef
{
    public GitHubRepoRef(string owner, string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(owner);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Owner = owner;
        Name = name;
    }

    public string Owner { get; }

    public string Name { get; }

    public override string ToString() => $"{Owner}/{Name}";
}
