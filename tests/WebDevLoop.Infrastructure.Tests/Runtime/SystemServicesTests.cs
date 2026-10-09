using WebDevLoop.Core.Domain;
using WebDevLoop.Infrastructure.Runtime;

namespace WebDevLoop.Infrastructure.Tests.Runtime;

public sealed class SystemServicesTests
{
    [Fact]
    public void System_clock_reports_the_current_utc_time()
    {
        DateTimeOffset before = DateTimeOffset.UtcNow;

        DateTimeOffset now = new SystemClock().UtcNow;

        Assert.InRange(now, before, DateTimeOffset.UtcNow);
        Assert.Equal(TimeSpan.Zero, now.Offset);
    }

    [Fact]
    public void Generated_ids_are_unique_branch_safe_segments_with_a_kind_prefix()
    {
        var ids = new GuidIdGenerator();

        RunId[] runs = [.. Enumerable.Range(0, 100).Select(_ => ids.NewRunId())];
        TicketRunId ticket = ids.NewTicketRunId();
        StepRunId step = ids.NewStepRunId();

        Assert.Equal(runs.Length, runs.Distinct().Count());
        Assert.All(runs, run => Assert.Matches("^r[0-9a-f]{12}$", run.Value));
        Assert.Matches("^t[0-9a-f]{12}$", ticket.Value);
        Assert.Matches("^s[0-9a-f]{12}$", step.Value);
        Assert.Equal($"webdevloop-{step.Value}", ids.NewAgentSessionId(step).Value);
    }
}
