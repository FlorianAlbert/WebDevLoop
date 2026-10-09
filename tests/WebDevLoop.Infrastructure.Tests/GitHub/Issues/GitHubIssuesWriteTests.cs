using System.Net;
using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Ports;
using WebDevLoop.Infrastructure.GitHub.Issues;
using static WebDevLoop.Infrastructure.Tests.GitHub.Issues.FakeIssuesApiHandler;

namespace WebDevLoop.Infrastructure.Tests.GitHub.Issues;

public sealed class GitHubIssuesWriteTests : GitHubIssuesTestBase
{
    private static string CommentsPath(int number) => $"{IssuePath(number)}/comments";

    private const string IssuesPath = "/repos/acme/widgets/issues";

    // The parent (issue 1) was created at a known time so the repository-wide dedupe search can be bounded by it.
    private void ScriptRepoIssues(params object[] issues)
    {
        Api.Get(IssuePath(1), new { number = 1, id = 100L, node_id = "I_100", title = "Spec", body = "", state = "open", created_at = "2026-01-02T03:04:05Z" });
        Api.Get(IssuesPath, issues);
    }

    private void ScriptRelationRejectedAsDuplicate(string path)
    {
        Api.Respond(HttpMethod.Post, path, HttpStatusCode.UnprocessableEntity, new { message = "Validation Failed" });
    }

    [Fact]
    public async Task AddBlockedBy_serializes_the_blocker_database_id_not_the_issue_number()
    {
        Api.Respond(HttpMethod.Post, BlockedByPath(4), HttpStatusCode.Created, Issue(3, 300));

        await CreateSut().AddBlockedByAsync(Ref(4, 400), Ref(3, 300), CancellationToken.None);

        IssuesApiRequest post = Assert.Single(Api.RequestsTo(HttpMethod.Post, BlockedByPath(4)));
        Assert.Equal(300, post.Json.GetProperty("issue_id").GetInt64());
        Assert.False(post.Json.TryGetProperty("issue_number", out _));
    }

    [Fact]
    public async Task AddBlockedBy_looks_up_the_blocker_database_id_when_the_reference_has_none()
    {
        Api.Get(IssuePath(3), Issue(3, 300));
        Api.Respond(HttpMethod.Post, BlockedByPath(4), HttpStatusCode.Created, Issue(3, 300));

        await CreateSut().AddBlockedByAsync(Ref(4), Ref(3), CancellationToken.None);

        Assert.Equal(300, Assert.Single(Api.RequestsTo(HttpMethod.Post, BlockedByPath(4))).Json.GetProperty("issue_id").GetInt64());
    }

    [Fact]
    public async Task AddBlockedBy_is_idempotent_when_the_relation_already_exists()
    {
        ScriptRelationRejectedAsDuplicate(BlockedByPath(4));
        Api.Get(BlockedByPath(4), new[] { Issue(3, 300) });

        await CreateSut().AddBlockedByAsync(Ref(4, 400), Ref(3, 300), CancellationToken.None);
    }

    [Fact]
    public async Task AddBlockedBy_rejection_without_an_existing_relation_is_an_error()
    {
        ScriptRelationRejectedAsDuplicate(BlockedByPath(4));
        Api.Get(BlockedByPath(4), Array.Empty<object>());

        GitHubApiException error = await Assert.ThrowsAsync<GitHubApiException>(
            () => CreateSut().AddBlockedByAsync(Ref(4, 400), Ref(3, 300), CancellationToken.None));

        Assert.Equal(GitHubApiErrorKind.Invalid, error.Kind);
    }

    [Fact]
    public async Task AddSubIssue_serializes_the_child_database_id_without_replacing_its_parent()
    {
        Api.Respond(HttpMethod.Post, SubIssuesPath(1), HttpStatusCode.Created, Issue(1, 100));

        await CreateSut().AddSubIssueAsync(Ref(1, 100), Ref(2, 200), CancellationToken.None);

        IssuesApiRequest post = Assert.Single(Api.RequestsTo(HttpMethod.Post, SubIssuesPath(1)));
        Assert.Equal(200, post.Json.GetProperty("sub_issue_id").GetInt64());
        Assert.False(post.Json.GetProperty("replace_parent").GetBoolean());
    }

    [Fact]
    public async Task AddSubIssue_is_idempotent_when_the_child_is_already_linked()
    {
        ScriptRelationRejectedAsDuplicate(SubIssuesPath(1));
        Api.Get(SubIssuesPath(1), new[] { Issue(2, 200) });

        await CreateSut().AddSubIssueAsync(Ref(1, 100), Ref(2, 200), CancellationToken.None);
    }

    [Fact]
    public async Task CreateFindingIssue_reuses_an_existing_sub_issue_with_the_same_fingerprint()
    {
        var fingerprint = new FindingFingerprint("Missing null check");
        Api.Get(SubIssuesPath(1), new[] { Issue(5, 500, "Existing", $"text\n{FindingFingerprintMarker.Render(fingerprint)}") });
        Api.Get(BlockedByPath(5), Array.Empty<object>());

        IssueSnapshot created = await CreateSut().CreateFindingIssueAsync(
            new FindingIssueDraft(Ref(1, 100), "New", "Body", new FindingFingerprint("missing  NULL check")), CancellationToken.None);

        Assert.Equal(5, created.Ref.Number);
        Assert.DoesNotContain(Api.Requests, request => request.Method == HttpMethod.Post);
    }

    [Fact]
    public async Task CreateFindingIssue_embeds_the_fingerprint_and_links_the_new_issue_to_its_parent()
    {
        var fingerprint = new FindingFingerprint("Missing null check");
        string issuesPath = "/repos/acme/widgets/issues";
        Api.Get(SubIssuesPath(1), Array.Empty<object>());
        ScriptRepoIssues(Array.Empty<object>());
        Api.Respond(HttpMethod.Post, issuesPath, HttpStatusCode.Created, Issue(5, 500, "Fix null check", "Details"));
        Api.Respond(HttpMethod.Post, SubIssuesPath(1), HttpStatusCode.Created, Issue(1, 100));
        Api.Get(BlockedByPath(5), Array.Empty<object>());

        IssueSnapshot created = await CreateSut().CreateFindingIssueAsync(
            new FindingIssueDraft(Ref(1, 100), "Fix null check", "Details", fingerprint), CancellationToken.None);

        Assert.Equal(5, created.Ref.Number);
        IssuesApiRequest create = Assert.Single(Api.RequestsTo(HttpMethod.Post, issuesPath));
        Assert.Equal("Fix null check", create.Json.GetProperty("title").GetString());
        string body = create.Json.GetProperty("body").GetString()!;
        Assert.StartsWith("Details", body);
        Assert.True(FindingFingerprintMarker.IsPresentIn(body, fingerprint));
        IssuesApiRequest link = Assert.Single(Api.RequestsTo(HttpMethod.Post, SubIssuesPath(1)));
        Assert.Equal(500, link.Json.GetProperty("sub_issue_id").GetInt64());
    }

    [Fact]
    public async Task CreateFindingIssue_links_an_unlinked_issue_left_by_an_earlier_attempt_instead_of_creating_a_duplicate()
    {
        var fingerprint = new FindingFingerprint("Missing null check");
        Api.Get(SubIssuesPath(1), Array.Empty<object>());
        ScriptRepoIssues(
            new { number = 9, id = 900L, node_id = "I_900", title = "A pull request", body = FindingFingerprintMarker.Render(fingerprint), state = "open", pull_request = new { url = "x" } },
            new { number = 6, id = 600L, node_id = "I_600", title = "Unrelated", body = "nothing", state = "open" },
            new { number = 5, id = 500L, node_id = "I_500", title = "Orphan", body = $"Details\n\n{FindingFingerprintMarker.Render(fingerprint)}", state = "open" });
        Api.Respond(HttpMethod.Post, IssuesPath, HttpStatusCode.Created, Issue(7, 700, "Duplicate"));
        Api.Respond(HttpMethod.Post, SubIssuesPath(1), HttpStatusCode.Created, Issue(1, 100));
        Api.Get(BlockedByPath(5), Array.Empty<object>());

        IssueSnapshot reused = await CreateSut().CreateFindingIssueAsync(
            new FindingIssueDraft(Ref(1, 100), "Fix null check", "Details", fingerprint), CancellationToken.None);

        Assert.Equal(5, reused.Ref.Number);
        Assert.Empty(Api.RequestsTo(HttpMethod.Post, IssuesPath));
        IssuesApiRequest link = Assert.Single(Api.RequestsTo(HttpMethod.Post, SubIssuesPath(1)));
        Assert.Equal(500, link.Json.GetProperty("sub_issue_id").GetInt64());
    }

    [Fact]
    public async Task CreateFindingIssue_only_searches_repository_issues_updated_since_the_parent_was_created()
    {
        var fingerprint = new FindingFingerprint("Missing null check");
        Api.Get(SubIssuesPath(1), Array.Empty<object>());
        ScriptRepoIssues(Array.Empty<object>());
        Api.Respond(HttpMethod.Post, IssuesPath, HttpStatusCode.Created, Issue(5, 500));
        Api.Respond(HttpMethod.Post, SubIssuesPath(1), HttpStatusCode.Created, Issue(1, 100));
        Api.Get(BlockedByPath(5), Array.Empty<object>());

        await CreateSut().CreateFindingIssueAsync(
            new FindingIssueDraft(Ref(1, 100), "Fix null check", "Details", fingerprint), CancellationToken.None);

        IssuesApiRequest search = Assert.Single(Api.Requests, request => request.Method == HttpMethod.Get && request.Path == IssuesPath);
        Assert.Contains("since=2026-01-02T03:04:05Z", search.PathAndQuery);
        Assert.Contains("state=all", search.PathAndQuery);
    }

    [Fact]
    public async Task CreateFindingIssue_ignores_a_matching_issue_that_already_belongs_to_another_parent()
    {
        var fingerprint = new FindingFingerprint("Missing null check");
        Api.Get(SubIssuesPath(1), Array.Empty<object>());
        ScriptRepoIssues(
            new { number = 4, id = 400L, node_id = "I_400", title = "Other spec's finding", body = FindingFingerprintMarker.Render(fingerprint), state = "open", parent_issue_url = "https://api.github.com/repos/acme/widgets/issues/2" });
        Api.Respond(HttpMethod.Post, IssuesPath, HttpStatusCode.Created, Issue(5, 500));
        Api.Respond(HttpMethod.Post, SubIssuesPath(1), HttpStatusCode.Created, Issue(1, 100));
        Api.Get(BlockedByPath(5), Array.Empty<object>());

        IssueSnapshot created = await CreateSut().CreateFindingIssueAsync(
            new FindingIssueDraft(Ref(1, 100), "Fix null check", "Details", fingerprint), CancellationToken.None);

        Assert.Equal(5, created.Ref.Number);
        Assert.Single(Api.RequestsTo(HttpMethod.Post, IssuesPath));
    }

    [Fact]
    public async Task Comment_posts_the_body()
    {
        Api.Respond(HttpMethod.Post, CommentsPath(7), HttpStatusCode.Created, new { id = 1 });

        await CreateSut().CommentAsync(Ref(7), "Hello", CancellationToken.None);

        Assert.Equal("Hello", Assert.Single(Api.RequestsTo(HttpMethod.Post, CommentsPath(7))).Json.GetProperty("body").GetString());
    }

    [Theory]
    [InlineData(IssueCloseReason.Completed, "completed")]
    [InlineData(IssueCloseReason.NotPlanned, "not_planned")]
    public async Task Close_sets_state_and_state_reason(IssueCloseReason reason, string expectedReason)
    {
        Api.Get(IssuePath(7), Issue(7, 700));
        Api.Respond(HttpMethod.Patch, IssuePath(7), HttpStatusCode.OK, Issue(7, 700, state: "closed"));

        await CreateSut().CloseAsync(Ref(7), reason, CancellationToken.None);

        IssuesApiRequest patch = Assert.Single(Api.RequestsTo(HttpMethod.Patch, IssuePath(7)));
        Assert.Equal("closed", patch.Json.GetProperty("state").GetString());
        Assert.Equal(expectedReason, patch.Json.GetProperty("state_reason").GetString());
    }

    [Fact]
    public async Task Close_is_idempotent_for_an_already_closed_issue()
    {
        Api.Get(IssuePath(7), Issue(7, 700, state: "closed"));

        await CreateSut().CloseAsync(Ref(7), IssueCloseReason.Completed, CancellationToken.None);

        Assert.DoesNotContain(Api.Requests, request => request.Method == HttpMethod.Patch);
    }
}
