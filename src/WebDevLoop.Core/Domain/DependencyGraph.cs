namespace WebDevLoop.Core.Domain;

public static class DependencyGraph
{
    public static void EnsureCanAdd<T>(IEnumerable<DependencyEdge<T>> existing, DependencyEdge<T> candidate)
        where T : notnull
    {
        if (EqualityComparer<T>.Default.Equals(candidate.Blocked, candidate.Blocking))
        {
            throw new DependencyCycleException($"'{candidate.Blocked}' cannot depend on itself.");
        }

        // The candidate closes a cycle when the blocker already (transitively) waits for the blocked node.
        if (WaitsFor(existing, candidate.Blocking, candidate.Blocked))
        {
            throw new DependencyCycleException(
                $"'{candidate.Blocked}' blocked by '{candidate.Blocking}' would create a dependency cycle.");
        }
    }

    public static void EnsureAcyclic<T>(IEnumerable<DependencyEdge<T>> edges)
        where T : notnull
    {
        var accepted = new List<DependencyEdge<T>>();

        foreach (DependencyEdge<T> edge in edges)
        {
            EnsureCanAdd(accepted, edge);
            accepted.Add(edge);
        }
    }

    private static bool WaitsFor<T>(IEnumerable<DependencyEdge<T>> edges, T from, T target)
        where T : notnull
    {
        ILookup<T, T> blockersByBlocked = edges.ToLookup(edge => edge.Blocked, edge => edge.Blocking);
        var visited = new HashSet<T>();
        var pending = new Stack<T>();
        pending.Push(from);

        while (pending.TryPop(out T? node))
        {
            if (EqualityComparer<T>.Default.Equals(node, target))
            {
                return true;
            }

            if (visited.Add(node))
            {
                foreach (T blocker in blockersByBlocked[node])
                {
                    pending.Push(blocker);
                }
            }
        }

        return false;
    }
}
