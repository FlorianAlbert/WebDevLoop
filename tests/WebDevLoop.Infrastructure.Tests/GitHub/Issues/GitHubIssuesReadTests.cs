using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Ports;
using WebDevLoop.Infrastructure.GitHub.Issues;
using static WebDevLoop.Infrastructure.Tests.GitHub.Issues.FakeIssuesApiHandler;

namespace WebDevLoop.Infrastructure.Tests.GitHub.Issues;

public sealed class GitHubIssuesReadTests : GitHubIssuesTestBase
{
    private void ScriptIssue(int number, long id, string title = "Title", string body = "", string state = "open", params (int Number, long Id)[] blockedBy)
    {
        Api.Get(IssuePath(number), Issue(number, id, title, body, state));
        Api.Get(BlockedByPath(number), blockedBy.Select(blocker => Issue(blocker.Number, blocker.Id)).ToArray());
    }

    private void ScriptSubIssues(int parent, params (int Number, long Id, string Body)[] children) =>
        Api.Get(SubIssuesPath(parent), children.Select(child => Issue(child.Number, child.Id, $"Ticket {child.Number}", child.Body)).ToArray());

    [Fact]
    public async Task GetIssue_maps_fields_ids_and_native_blocked_by_relations()
    {
        ScriptIssue(7, 700, "Do it", "Details", "closed", (3, 300));

        IssueSnapshot snapshot = await CreateSut().GetIssueAsync(Ref(7), CancellationToken.None);

        Assert.Equal("Do it", snapshot.Title);
        Assert.Equal("Details", snapshot.Body);
        Assert.Equal(IssueState.Closed, snapshot.State);
        Assert.Equal(7, snapshot.Ref.Number);
        Assert.Equal(700, snapshot.Ref.DatabaseId);
        Assert.Equal("I_700", snapshot.Ref.NodeId);
        IssueRef blocker = Assert.Single(snapshot.BlockedBy);
        Assert.Equal(("acme", "widgets", 3, 300L), (blocker.Owner, blocker.Repo, blocker.Number, blocker.DatabaseId));
    }

    [Fact]
    public async Task GetIssue_follows_pagination_links_of_the_blocked_by_list()
    {
        Api.Get(IssuePath(7), Issue(7, 700));
        Api.Get(BlockedByPath(7) + "?per_page=100", new[] { Issue(3, 300) },
            ("Link", $"<https://api.github.com{BlockedByPath(7)}?per_page=100&page=2>; rel=\"next\""));
        Api.Get(BlockedByPath(7) + "?per_page=100&page=2", new[] { Issue(4, 400) });

        IssueSnapshot snapshot = await CreateSut().GetIssueAsync(Ref(7), CancellationToken.None);

        Assert.Equal([3, 4], snapshot.BlockedBy.Select(blocker => blocker.Number));
    }

    [Fact]
    public async Task GetSpecGraph_combines_parent_sub_issues_and_dependencies_into_a_dag()
    {
        ScriptIssue(1, 100, "Spec", "Spec body", "open", (900, 9000));
        ScriptSubIssues(1, (2, 200, ""), (3, 300, ""), (4, 400, ""));
        ScriptIssue(2, 200);
        ScriptIssue(3, 300, blockedBy: (2, 200));
        ScriptIssue(4, 400, blockedBy: [(2, 200), (3, 300), (50, 5000)]);

        SpecIssueGraph graph = await CreateSut().GetSpecGraphAsync(Ref(1), CancellationToken.None);

        Assert.Equal("Spec", graph.Spec.Title);
        Assert.Equal([900], graph.Spec.BlockedBy.Select(blocker => blocker.Number));
        Assert.Equal([2, 3, 4], graph.Tickets.Select(ticket => ticket.Ref.Number));
        Assert.Equal(
            [(3, 2), (4, 2), (4, 3)],
            graph.TicketDependencies.Select(edge => (edge.Blocked.Number, edge.Blocking.Number)).Order());
        Assert.Contains(50, graph.Tickets[2].BlockedBy.Select(blocker => blocker.Number));
    }

    [Fact]
    public async Task GetSpecGraph_rejects_cyclic_ticket_dependencies()
    {
        ScriptIssue(1, 100);
        ScriptSubIssues(1, (2, 200, ""), (3, 300, ""));
        ScriptIssue(2, 200, blockedBy: (3, 300));
        ScriptIssue(3, 300, blockedBy: (2, 200));

        await Assert.ThrowsAsync<DependencyCycleException>(() => CreateSut().GetSpecGraphAsync(Ref(1), CancellationToken.None));
    }

    [Fact]
    public async Task FindFindingIssue_detects_an_existing_fingerprint_among_sub_issues_ignoring_case_and_whitespace()
    {
        var fingerprint = new FindingFingerprint("Missing   null check");
        ScriptSubIssues(
            1,
            (2, 200, "unrelated"),
            (3, 300, $"Finding\n\n{FindingFingerprintMarker.Render(fingerprint)}"),
            (4, 400, FindingFingerprintMarker.Render(new FindingFingerprint("something else"))));
        ScriptIssue(3, 300, "Ticket 3", "body");

        IssueSnapshot? found = await CreateSut().FindFindingIssueAsync(Ref(1), new FindingFingerprint("missing null CHECK"), CancellationToken.None);

        Assert.Equal(3, found?.Ref.Number);
    }

    [Fact]
    public async Task FindFindingIssue_returns_null_when_no_sub_issue_carries_the_fingerprint()
    {
        ScriptSubIssues(1, (2, 200, "unrelated"));

        IssueSnapshot? found = await CreateSut().FindFindingIssueAsync(Ref(1), new FindingFingerprint("absent"), CancellationToken.None);

        Assert.Null(found);
    }

    [Fact]
    public async Task ListComments_returns_every_comment_body_across_pages_oldest_first()
    {
        string comments = $"{IssuePath(7)}/comments";
        Api.Get(comments + "?per_page=100", new[] { new { id = 1, body = "first" } },
            ("Link", $"<https://api.github.com{comments}?per_page=100&page=2>; rel=\"next\""));
        Api.Get(comments + "?per_page=100&page=2", new[] { new { id = 2, body = "second" } });

        IReadOnlyList<string> bodies = await CreateSut().ListCommentsAsync(Ref(7), CancellationToken.None);

        Assert.Equal(["first", "second"], bodies);
    }
}
