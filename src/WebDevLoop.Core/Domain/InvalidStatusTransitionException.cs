namespace WebDevLoop.Core.Domain;

public sealed class InvalidStatusTransitionException : InvalidOperationException
{
    public InvalidStatusTransitionException(string entityName, Enum from, Enum to)
        : base($"{entityName} cannot transition from {from} to {to}.")
    {
        EntityName = entityName;
        From = from;
        To = to;
    }

    public string EntityName { get; }

    public Enum From { get; }

    public Enum To { get; }
}
