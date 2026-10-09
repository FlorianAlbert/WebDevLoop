using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Events;
using WebDevLoop.Core.Orchestration.Completion.Testing;

namespace WebDevLoop.Core.Orchestration.Completion.ReadyAndMerge;

/// <summary>
/// Event-bus subscriber for workflow steps 13–14: a passed tester run (<see cref="SpecTestingPassed"/>, possibly re-emitted
/// after a restart) or an aborted spec launches <see cref="SpecCompletionService"/> in the background. Completion acts on the
/// spec's persisted state, so redeliveries are harmless.
/// </summary>
public sealed class CompletionEventHandler(ICompletionLauncher launcher)
{
    public Task HandleAsync(EventEnvelope envelope, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        switch (envelope.Event)
        {
            case SpecTestingPassed passed:
                launcher.Launch(new CompletionAssignment(passed.SpecRunId));
                break;
            case SpecRunStatusChanged { To: SpecRunStatus.Aborted } aborted:
                launcher.Launch(new CompletionAssignment(aborted.SpecRunId));
                break;
        }

        return Task.CompletedTask;
    }
}
