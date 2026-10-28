using System.Text.Json;
using System.Text.Json.Nodes;
using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Ports;
using WebDevLoop.Infrastructure.GitHub.Issues;
using static WebDevLoop.Infrastructure.Tests.GitHub.Issues.FakeIssuesApiHandler;

namespace WebDevLoop.Infrastructure.Tests.GitHub.Issues;

public sealed class GitHubIssuesReadTests : GitHubIssuesTestBase
{
    [Fact]
    public async Task paginated_native_blockers_keep_external_repository_identity_and_current_state_without_a_local_graph()
    {
        var firstPage = new JsonArray(JsonSerializer.SerializeToNode(Issue(3, 300)));
        var external = new JsonObject
        {
            ["number"] = 3, ["id"] = 900, ["title"] = "Outside this repository",
            ["repository_url"] = "https://api.github.com/repos/another-owner/library",
            ["state"] = "open", ["labels"] = new JsonArray(),
        };
        Api.Get(IssuePath(7), Issue(7, 700));
        Api.Get(BlockedByPath(7) + "?per_page=100", firstPage,
            ("Link", $"<https://api.github.com{BlockedByPath(7)}?per_page=100&page=2>; rel=\"next\""));
        Api.Get(BlockedByPath(7) + "?per_page=100&page=2", new JsonArray(external.DeepClone()));
        Api.Get(IssuePath(3), Issue(3, 300, state: "closed"));
        Api.Get(BlockedByPath(3), Array.Empty<object>());
        Api.Get("/repos/another-owner/library/issues/3", external);
        Api.Get("/repos/another-owner/library/issues/3/dependencies/blocked_by", Array.Empty<object>());
        GitHubIssues github = CreateSut();

        IssueSnapshot ticket = await github.GetIssueAsync(Ref(7), CancellationToken.None);
        IssueSnapshot local = await github.GetIssueAsync(ticket.BlockedBy[0], CancellationToken.None);
        IssueSnapshot crossRepository = await github.GetIssueAsync(ticket.BlockedBy[1], CancellationToken.None);
        external["state"] = "closed";
        IssueSnapshot closed = await github.GetIssueAsync(ticket.BlockedBy[1], CancellationToken.None);
        firstPage.Clear();

        Assert.Equal(2, ticket.BlockedBy.Count);
        Assert.Equal(IssueState.Closed, local.State);
        Assert.Equal(("another-owner", "library", 3), (crossRepository.Ref.Owner, crossRepository.Ref.Repo, crossRepository.Ref.Number));
        Assert.Empty(crossRepository.Labels);
        Assert.Equal(IssueState.Open, crossRepository.State);
        Assert.Equal(IssueState.Closed, closed.State);
        Assert.Single((await github.GetIssueAsync(Ref(7), CancellationToken.None)).BlockedBy);
    }

    [Fact]
    public async Task later_queries_observe_parent_and_child_opt_out_and_closure_instead_of_cached_snapshots()
    {
        var parent = new JsonObject
        {
            ["number"] = 1, ["title"] = "Spec", ["state"] = "open",
            ["labels"] = new JsonArray(new JsonObject { ["name"] = "webdevloop" }),
        };
        var child = new JsonObject
        {
            ["number"] = 2, ["title"] = "Ticket", ["state"] = "open",
            ["labels"] = new JsonArray(new JsonObject { ["name"] = "webdevloop" }),
        };
        Api.Get(IssuePath(1), parent);
        Api.Get(SubIssuesPath(1), new JsonArray(child));
        Api.Get(BlockedByPath(1), Array.Empty<object>());
        Api.Get(BlockedByPath(2), Array.Empty<object>());
        GitHubIssues github = CreateSut();
        IssueSnapshot initialParent = await github.GetIssueAsync(Ref(1), CancellationToken.None);
        IssueSnapshot initialChild = Assert.Single(await github.GetSubIssuesAsync(Ref(1), CancellationToken.None));

        parent["labels"] = new JsonArray();
        parent["state"] = "closed";
        child["labels"] = new JsonArray();
        child["state"] = "closed";
        IssueSnapshot currentParent = await github.GetIssueAsync(Ref(1), CancellationToken.None);
        IssueSnapshot currentChild = Assert.Single(await github.GetSubIssuesAsync(Ref(1), CancellationToken.None));

        Assert.Equal(["webdevloop"], initialParent.Labels);
        Assert.Equal(["webdevloop"], initialChild.Labels);
        Assert.Equal(IssueState.Closed, currentParent.State);
        Assert.Equal(IssueState.Closed, currentChild.State);
        Assert.Empty(currentParent.Labels);
        Assert.Empty(currentChild.Labels);
    }

    [Fact]
    public async Task parent_and_paginated_sub_issues_retain_their_own_current_labels_and_state()
    {
        Api.Get(IssuePath(1), Issue(1, 100, labels: ["webdevloop"]));
        Api.Get(BlockedByPath(1), Array.Empty<object>());
        Api.Get(SubIssuesPath(1) + "?per_page=100", new[] { Issue(2, 200, labels: ["ready-for-agent"]) },
            ("Link", $"<https://api.github.com{SubIssuesPath(1)}?per_page=100&page=2>; rel=\"next\""));
        Api.Get(SubIssuesPath(1) + "?per_page=100&page=2", new[]
        {
            Issue(3, 300, state: "closed", labels: ["webdevloop"]),
            Issue(4, 400, labels: ["webdevloop", "ready-for-human"]),
        });
        foreach (int child in new[] { 2, 3, 4 })
        {
            Api.Get(BlockedByPath(child), Array.Empty<object>());
        }

        GitHubIssues github = CreateSut();
        IssueSnapshot parent = await github.GetIssueAsync(Ref(1), CancellationToken.None);
        IReadOnlyList<IssueSnapshot> children = await github.GetSubIssuesAsync(Ref(1), CancellationToken.None);

        Assert.Equal(["webdevloop"], parent.Labels);
        Assert.Equal([2, 3, 4], children.Select(child => child.Ref.Number));
        Assert.Equal(["ready-for-agent"], children[0].Labels);
        Assert.Equal(IssueState.Closed, children[1].State);
        Assert.Equal(["webdevloop"], children[1].Labels);
        Assert.Equal(IssueState.Open, children[2].State);
        Assert.Equal(["webdevloop", "ready-for-human"], children[2].Labels);
    }

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
