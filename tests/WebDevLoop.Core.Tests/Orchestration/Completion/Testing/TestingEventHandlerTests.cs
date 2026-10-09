using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Events;
using WebDevLoop.Core.Orchestration.Completion.Testing;
using WebDevLoop.Core.Tests.Orchestration.TicketExecution;

namespace WebDevLoop.Core.Tests.Orchestration.Completion.Testing;

public sealed class TestingEventHandlerTests
{
    private static readonly RunId Spec = new("run-7");

    private readonly TestTestingLauncher _launcher = new();

    [Fact]
    public async Task Spec_entering_testing_launches_its_tester_run()
    {
        await HandleAsync(new SpecRunStatusChanged(Spec, 3, SpecRunStatus.ParentReviewing, SpecRunStatus.Testing, TicketExecutionFixture.T0));

        Assert.Equal([new TestingAssignment(Spec)], _launcher.Launched);
    }

    [Theory]
    [InlineData(SpecRunStatus.Running, SpecRunStatus.ParentReviewing)]
    [InlineData(SpecRunStatus.Testing, SpecRunStatus.Running)]
    [InlineData(SpecRunStatus.Testing, SpecRunStatus.NeedsAttention)]
    public async Task Other_transitions_launch_nothing(SpecRunStatus from, SpecRunStatus to)
    {
        await HandleAsync(new SpecRunStatusChanged(Spec, 3, from, to, TicketExecutionFixture.T0));
        await HandleAsync(new SpecTestingPassed(Spec, 3, 1, TicketExecutionFixture.T0));

        Assert.Empty(_launcher.Launched);
    }

    private Task HandleAsync(WorkflowEvent workflowEvent) =>
        new TestingEventHandler(_launcher).HandleAsync(new EventEnvelope(1, workflowEvent), TestingFixture.Token);
}
