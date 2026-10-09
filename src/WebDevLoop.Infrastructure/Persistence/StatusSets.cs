namespace WebDevLoop.Infrastructure.Persistence;

/// <summary>Derives status sets from the domain rules so queries and filtered indexes never drift from the state machine.</summary>
internal static class StatusSets
{
    public static TStatus[] Terminal<TStatus>(Func<TStatus, bool> isTerminal)
        where TStatus : struct, Enum => Matching(isTerminal);

    public static TStatus[] Active<TStatus>(Func<TStatus, bool> isActive)
        where TStatus : struct, Enum => Matching(isActive);

    private static TStatus[] Matching<TStatus>(Func<TStatus, bool> predicate)
        where TStatus : struct, Enum => Enum.GetValues<TStatus>().Where(predicate).ToArray();
}
