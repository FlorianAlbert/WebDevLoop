using WebDevLoop.Core.Domain;

namespace WebDevLoop.Core.Tests.Domain;

public sealed class DependencyGraphTests
{
    private static DependencyEdge<string> Edge(string blocked, string blocking) => new(blocked, blocking);

    [Fact]
    public void self_link_is_rejected()
    {
        Assert.Throws<DependencyCycleException>(() => DependencyGraph.EnsureCanAdd([], Edge("a", "a")));
    }

    [Fact]
    public void direct_two_node_cycle_is_rejected()
    {
        Assert.Throws<DependencyCycleException>(() => DependencyGraph.EnsureCanAdd([Edge("a", "b")], Edge("b", "a")));
    }

    [Fact]
    public void transitive_cycle_is_rejected()
    {
        DependencyEdge<string>[] existing = [Edge("a", "b"), Edge("b", "c")];

        Assert.Throws<DependencyCycleException>(() => DependencyGraph.EnsureCanAdd(existing, Edge("c", "a")));
    }

    [Fact]
    public void diamond_shaped_dag_is_accepted()
    {
        DependencyEdge<string>[] existing = [Edge("d", "b"), Edge("d", "c"), Edge("b", "a")];

        DependencyGraph.EnsureCanAdd(existing, Edge("c", "a"));
    }

    [Fact]
    public void ensure_acyclic_rejects_a_cyclic_edge_set()
    {
        Assert.Throws<DependencyCycleException>(() =>
            DependencyGraph.EnsureAcyclic([Edge("a", "b"), Edge("b", "c"), Edge("c", "a")]));
    }

    [Fact]
    public void ensure_acyclic_rejects_self_links()
    {
        Assert.Throws<DependencyCycleException>(() => DependencyGraph.EnsureAcyclic([Edge("a", "a")]));
    }

    [Fact]
    public void ensure_acyclic_accepts_a_dag_and_an_empty_set()
    {
        DependencyGraph.EnsureAcyclic<string>([]);
        DependencyGraph.EnsureAcyclic([Edge("b", "a"), Edge("c", "a"), Edge("c", "b")]);
    }
}
