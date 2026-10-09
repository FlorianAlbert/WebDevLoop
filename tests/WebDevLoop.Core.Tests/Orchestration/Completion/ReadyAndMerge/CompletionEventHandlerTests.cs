using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Events;
using WebDevLoop.Core.Orchestration.Completion.ReadyAndMerge;
using WebDevLoop.Core.Orchestration.Completion.Testing;

namespace WebDevLoop.Core.Tests.Orchestration.Completion.ReadyAndMerge;

public sealed class CompletionEventHandlerTests
{
    private static readonly DateTimeOffset At = ReadyAndMergeFixture.T0;
    private static readonly RunId Run = new("run-7");

    private readonly RecordingCompletionLauncher _launcher = new();

    private static CancellationToken Token => ReadyAndMergeFixture.Token;

    [Fact]
    public async Task A_testing_pass_launches_completion_every_time_it_is_delivered()
    {
        var handler = new CompletionEventHandler(_launcher);

        await handler.HandleAsync(new EventEnvelope(1, new SpecTestingPassed(Run, 3, 1, At)), Token);
        await handler.HandleAsync(new EventEnvelope(2, new SpecTestingPassed(Run, 3, 1, At)), Token);

        Assert.Equal([new CompletionAssignment(Run), new CompletionAssignment(Run)], _launcher.Launched);
    }

    [Fact]
    public async Task An_aborted_spec_launches_completion_to_clean_up_its_worktrees()
    {
        var handler = new CompletionEventHandler(_launcher);

        await handler.HandleAsync(new EventEnvelope(1, new SpecRunStatusChanged(Run, 3, SpecRunStatus.Running, SpecRunStatus.Aborted, At)), Token);

        Assert.Equal([new CompletionAssignment(Run)], _launcher.Launched);
    }

    [Theory]
    [InlineData(SpecRunStatus.Testing)]
    [InlineData(SpecRunStatus.ReadyForReview)]
    [InlineData(SpecRunStatus.AwaitingMerge)]
    [InlineData(SpecRunStatus.Completed)]
    [InlineData(SpecRunStatus.NeedsAttention)]
    public async Task Other_spec_transitions_launch_nothing(SpecRunStatus to)
    {
        var handler = new CompletionEventHandler(_launcher);

        await handler.HandleAsync(new EventEnvelope(1, new SpecRunStatusChanged(Run, 3, SpecRunStatus.Running, to, At)), Token);
        await handler.HandleAsync(new EventEnvelope(2, new FrontierReconciliationRequested(Run, At)), Token);

        Assert.Empty(_launcher.Launched);
    }
}
