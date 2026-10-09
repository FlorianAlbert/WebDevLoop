using WebDevLoop.Core.Domain;
using WebDevLoop.Infrastructure.GitHub.Pulls;

namespace WebDevLoop.Infrastructure.Tests.GitHub.Pulls;

public sealed class PullRequestMarkerTests
{
    private static readonly RunId Run = new("run-7");
    private static readonly TicketRunId Ticket = new("t.3_a");

    [Fact]
    public void Format_produces_a_stable_html_comment()
    {
        Assert.Equal("<!-- webdevloop:run=run-7 ticket=t.3_a -->", PullRequestMarker.Format(Run, Ticket));
    }

    [Fact]
    public void TryParse_round_trips_a_marker_embedded_in_a_larger_body()
    {
        string body = $"Closes #12\n\nSome text\n\n{PullRequestMarker.Format(Run, Ticket)}\n";

        var parsed = PullRequestMarker.TryParse(body);

        Assert.Equal((Run, Ticket), parsed);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Closes #12")]
    [InlineData("<!-- webdevloop:run=run-7 -->")]
    [InlineData("<!-- other:run=run-7 ticket=t1 -->")]
    [InlineData("<!-- webdevloop:run=a..b ticket=t1 -->")]
    public void TryParse_returns_null_without_a_valid_marker(string? body)
    {
        Assert.Null(PullRequestMarker.TryParse(body));
    }

    [Fact]
    public void TryParse_distinguishes_different_tickets()
    {
        var other = new TicketRunId("t4");

        Assert.NotEqual(
            PullRequestMarker.TryParse(PullRequestMarker.Format(Run, Ticket)),
            PullRequestMarker.TryParse(PullRequestMarker.Format(Run, other)));
    }
}
