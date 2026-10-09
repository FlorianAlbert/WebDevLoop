using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Events;

namespace WebDevLoop.Core.Orchestration.Completion.Testing;

/// <summary>
/// Event-bus subscriber for workflow step 11: a spec entering <c>Testing</c> (after a clean parent-spec review, or a retry)
/// has its tester run launched in the background (<see cref="ITestingLauncher"/>).
/// </summary>
public sealed class TestingEventHandler(ITestingLauncher launcher)
{
    public Task HandleAsync(EventEnvelope envelope, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        if (envelope.Event is SpecRunStatusChanged { To: SpecRunStatus.Testing } testing)
        {
            launcher.Launch(new TestingAssignment(testing.SpecRunId));
        }

        return Task.CompletedTask;
    }
}
