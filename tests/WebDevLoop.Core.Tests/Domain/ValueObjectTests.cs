using WebDevLoop.Core.Domain;

namespace WebDevLoop.Core.Tests.Domain;

public sealed class ValueObjectTests
{
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("a/b")]
    [InlineData("a b")]
    [InlineData("..")]
    public void run_scoped_ids_reject_values_unsafe_for_branch_names(string value)
    {
        Assert.ThrowsAny<ArgumentException>(() => new RunId(value));
        Assert.ThrowsAny<ArgumentException>(() => new TicketRunId(value));
        Assert.ThrowsAny<ArgumentException>(() => new StepRunId(value));
    }

    [Fact]
    public void ids_with_equal_values_are_equal()
    {
        Assert.Equal(new RunId("r1"), new RunId("r1"));
        Assert.NotEqual(new RunId("r1"), new RunId("r2"));
        Assert.Equal("r1", new RunId("r1").ToString());
    }

    [Theory]
    [InlineData("")]
    [InlineData("/leading")]
    [InlineData("trailing/")]
    [InlineData("has space")]
    [InlineData("a..b")]
    [InlineData("a//b")]
    public void branch_name_rejects_invalid_values(string value)
    {
        Assert.ThrowsAny<ArgumentException>(() => new BranchName(value));
    }

    [Fact]
    public void branch_name_keeps_slashes()
    {
        Assert.Equal("stack/r1/t1", new BranchName("stack/r1/t1").Value);
    }

    [Theory]
    [InlineData("")]
    [InlineData("xyz")]
    [InlineData("abc123")]
    public void commit_sha_rejects_non_hex_or_wrong_length(string value)
    {
        Assert.ThrowsAny<ArgumentException>(() => new CommitSha(value));
    }

    [Fact]
    public void commit_sha_is_normalized_to_lower_case()
    {
        string upper = new string('A', 40);

        Assert.Equal(new string('a', 40), new CommitSha(upper).Value);
        Assert.Equal(new CommitSha(upper), new CommitSha(upper.ToLowerInvariant()));
    }

    [Fact]
    public void pull_request_number_must_be_positive()
    {
        Assert.ThrowsAny<ArgumentException>(() => new PullRequestNumber(0));
        Assert.Equal(7, new PullRequestNumber(7).Value);
    }

    [Fact]
    public void finding_fingerprint_is_normalized_for_stable_matching()
    {
        var first = new FindingFingerprint("  Missing   Null CHECK\nin Parser ");
        var second = new FindingFingerprint("missing null check in parser");

        Assert.Equal(second, first);
        Assert.Equal("missing null check in parser", first.Value);
    }

    [Fact]
    public void finding_fingerprint_rejects_blank_values()
    {
        Assert.ThrowsAny<ArgumentException>(() => new FindingFingerprint("  "));
    }

    [Fact]
    public void repo_ref_requires_owner_and_name_and_formats_as_slug()
    {
        Assert.ThrowsAny<ArgumentException>(() => new GitHubRepoRef("", "repo"));
        Assert.ThrowsAny<ArgumentException>(() => new GitHubRepoRef("owner", " "));
        Assert.Equal("owner/repo", new GitHubRepoRef("owner", "repo").ToString());
    }

    [Fact]
    public void issue_ref_requires_positive_number_and_keeps_github_ids()
    {
        Assert.ThrowsAny<ArgumentException>(() => new IssueRef("o", "r", 0));

        var issue = new IssueRef("o", "r", 12, "node", 99);

        Assert.Equal("o/r#12", issue.ToString());
        Assert.Equal("node", issue.NodeId);
        Assert.Equal(99, issue.DatabaseId);
    }
}
