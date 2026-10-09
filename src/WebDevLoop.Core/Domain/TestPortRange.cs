namespace WebDevLoop.Core.Domain;

public readonly record struct TestPortRange
{
    public const int MinPort = 1;
    public const int MaxPort = 65535;

    public TestPortRange(int start, int end)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(start, MinPort);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(end, MaxPort);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(start, end);
        Start = start;
        End = end;
    }

    public int Start { get; }

    public int End { get; }

    public bool Contains(int port) => port >= Start && port <= End;
}
