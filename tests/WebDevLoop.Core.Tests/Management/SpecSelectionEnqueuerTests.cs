using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Management;
using WebDevLoop.Core.Orchestration.SpecQueue;

namespace WebDevLoop.Core.Tests.Management;

public sealed class SpecSelectionEnqueuerTests
{
    [Fact]
    public async Task every_attempt_has_an_ordered_result_and_a_failure_does_not_hide_later_successes()
    {
        ISpecSelectionEnqueuer enqueuer = new SpecSelectionEnqueuer(new ScriptedEnqueuer());

        IReadOnlyList<SpecSelectionResult> results = await enqueuer.EnqueueAsync(7, [30, 10, 20], TestContext.Current.CancellationToken);

        Assert.Equal([30, 10, 20], results.Select(result => result.SpecIssueNumber));
        Assert.Equal([EnqueueOutcome.Queued, EnqueueOutcome.Ineligible, EnqueueOutcome.AlreadyQueued],
            results.Select(result => result.Outcome));
        Assert.Equal("Queued #30.", results[0].Message);
        Assert.Equal("Restore the webdevloop label.", results[1].Message);
        Assert.Equal(new RunId("existing-20"), results[2].SpecRunId);
    }

    private sealed class ScriptedEnqueuer : ISpecEnqueuer
    {
        public Task<EnqueueResult> EnqueueAsync(int repositoryId, int specIssueNumber, CancellationToken cancellationToken) =>
            Task.FromResult(specIssueNumber switch
            {
                30 => new EnqueueResult(EnqueueOutcome.Queued, new RunId("new-30")),
                10 => new EnqueueResult(EnqueueOutcome.Ineligible, Reason: "Restore the webdevloop label."),
                _ => new EnqueueResult(EnqueueOutcome.AlreadyQueued, new RunId("existing-20")),
            });
    }
}
