using WebDevLoop.Core.Agents;
using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Orchestration.Completion.ParentReview;
using WebDevLoop.Core.Orchestration.Completion.Testing;
using WebDevLoop.Core.Orchestration.Integration;
using WebDevLoop.Core.Orchestration.ReviewLoop;
using WebDevLoop.Core.Orchestration.TicketExecution;
using WebDevLoop.Core.Ports;

namespace WebDevLoop.Core.Orchestration.Recovery.AgentSteps;

/// <summary>
/// Hands agent work that nothing runs any more back to its launcher. Work is stalled when its owner is in a working status
/// without an active step and either a step of it was just interrupted, or nothing happened to it (no status change, no step
/// started or finished) since this process started or within the stall grace period (e.g. a runner died before starting its
/// step, or shutdown cancelled it):
/// <list type="bullet">
/// <item>Running specs: <c>Implementing</c> tickets relaunch the implementer (resuming an interrupted session);
/// <c>Reviewing</c> tickets relaunch the review loop; <c>FixingReviewFindings</c> tickets relaunch the review loop, which
/// resumes the interrupted fix; <c>Integrating</c> tickets with an interrupted conflict resolution relaunch the saga
/// (other integrating tickets are reconciled by saga recovery).</item>
/// <item><c>ParentReviewing</c> and <c>Testing</c> specs relaunch their runner (finding-ticket creation is idempotent).</item>
/// <item><c>Preparing</c> specs with an interrupted exploration are reported for preparation, which has no launcher.</item>
/// </list>
/// Every launcher is safe to launch twice, so a relaunch racing a live runner starts at most one step.
/// </summary>
public sealed class StalledWorkRelauncher(
    ISpecRunRepository specRuns,
    ITicketRunRepository ticketRuns,
    IStepRunRepository stepRuns,
    AgentStepRecoveryLaunchers launchers,
    IClock clock,
    ProcessBoot boot,
    AgentStepRecoveryOptions options)
{
    public async Task<RelaunchResult> RelaunchAsync(IReadOnlyCollection<InterruptedStep> interrupted, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(interrupted);
        var relaunched = new List<RecoveredWork>();
        var preparation = new List<RunId>();
        foreach (SpecRun spec in await specRuns.ListNonTerminalAsync(cancellationToken))
        {
            IReadOnlyList<StepRun> steps = await stepRuns.ListBySpecRunAsync(spec.Id, cancellationToken);
            InterruptedStep[] specInterruptions = interrupted.Where(step => step.SpecRunId == spec.Id).ToArray();
            if (spec.Status == SpecRunStatus.Running)
            {
                foreach (TicketRun ticket in await ticketRuns.ListBySpecRunAsync(spec.Id, cancellationToken))
                {
                    StepRun[] ticketSteps = steps.Where(step => step.TicketRunId == ticket.Id).ToArray();
                    bool wasInterrupted = specInterruptions.Any(step => step.TicketRunId == ticket.Id);
                    if (StalledTicketWork(ticket, ticketSteps, wasInterrupted) is { } work)
                    {
                        Launch(spec, work);
                        relaunched.Add(work);
                    }
                }
            }
            else if (StalledSpecWork(spec, steps, specInterruptions) is { } work)
            {
                Launch(spec, work);
                relaunched.Add(work);
            }
            else if (spec.Status == SpecRunStatus.Preparing && IsIdleAfterInterruption(steps, specInterruptions, StepKind.Explore))
            {
                preparation.Add(spec.Id);
            }
        }

        return new RelaunchResult(relaunched, preparation);
    }

    private RecoveredWork? StalledTicketWork(TicketRun ticket, StepRun[] steps, bool wasInterrupted)
    {
        if (steps.Any(step => step.IsActive))
        {
            return null;
        }

        bool stalled = wasInterrupted || IsStalled(LastActivity(ticket.UpdatedAt, steps));
        return ticket.Status switch
        {
            TicketRunStatus.Implementing when stalled =>
                new RecoveredWork(RecoveredWorkKind.Implementation, ticket.SpecRunId, ticket.Id, InterruptedSession(steps, StepKind.Implement)),
            TicketRunStatus.FixingReviewFindings when stalled =>
                new RecoveredWork(RecoveredWorkKind.ReviewLoop, ticket.SpecRunId, ticket.Id, InterruptedSession(steps, StepKind.Fix)),
            TicketRunStatus.Reviewing when stalled => new RecoveredWork(RecoveredWorkKind.ReviewLoop, ticket.SpecRunId, ticket.Id),
            TicketRunStatus.Integrating when wasInterrupted && LatestOf(steps, StepKind.ResolveConflict) is { } latest && StepInterruption.IsInterruption(latest) =>
                new RecoveredWork(RecoveredWorkKind.Integration, ticket.SpecRunId, ticket.Id),
            _ => null,
        };
    }

    private RecoveredWork? StalledSpecWork(SpecRun spec, IReadOnlyList<StepRun> steps, InterruptedStep[] interruptions)
    {
        if (SpecWorkFor(spec.Status) is not { } specWork)
        {
            return null;
        }

        (StepKind kind, RecoveredWorkKind work) = specWork;
        if (steps.Any(step => step.Kind == kind && step.IsActive))
        {
            return null;
        }

        bool stalled = interruptions.Any(step => step.Kind == kind) || IsStalled(LastActivity(spec.StatusChangedAt, steps));
        return stalled ? new RecoveredWork(work, spec.Id) : null;
    }

    private static (StepKind Step, RecoveredWorkKind Work)? SpecWorkFor(SpecRunStatus status) => status switch
    {
        SpecRunStatus.ParentReviewing => (StepKind.ParentReview, RecoveredWorkKind.ParentReview),
        SpecRunStatus.Testing => (StepKind.Test, RecoveredWorkKind.Testing),
        _ => null,
    };

    private static bool IsIdleAfterInterruption(IReadOnlyList<StepRun> steps, InterruptedStep[] interruptions, StepKind kind) =>
        interruptions.Any(step => step.Kind == kind && step.TicketRunId is null) && !steps.Any(step => step.Kind == kind && step.IsActive);

    /// <summary>Nothing happened to the owner since this process started, or within the grace period.</summary>
    private bool IsStalled(DateTimeOffset lastActivity) =>
        boot.IsBeforeBoot(lastActivity) || lastActivity <= clock.UtcNow - options.StallGracePeriod;

    private static DateTimeOffset LastActivity(DateTimeOffset ownerChangedAt, IEnumerable<StepRun> steps) =>
        steps.SelectMany(step => new[] { step.StartedAt, step.CompletedAt })
            .OfType<DateTimeOffset>()
            .Append(ownerChangedAt)
            .Max();

    /// <summary>The session of the latest step of <paramref name="kind"/>, when recovery interrupted it.</summary>
    private static AgentSessionId? InterruptedSession(IEnumerable<StepRun> steps, StepKind kind) =>
        LatestOf(steps, kind) is { CopilotSessionId: { } session } latest && StepInterruption.IsInterruption(latest)
            ? new AgentSessionId(session)
            : null;

    private static StepRun? LatestOf(IEnumerable<StepRun> steps, StepKind kind) =>
        steps.Where(step => step.Kind == kind).OrderBy(step => step.StartedAt).ThenBy(step => step.Attempt).LastOrDefault();

    private void Launch(SpecRun spec, RecoveredWork work)
    {
        switch (work.Kind)
        {
            case RecoveredWorkKind.Implementation:
                launchers.Implementation.Launch(new ImplementationAssignment(work.SpecRunId, work.TicketRunId!.Value, work.ResumeSessionId));
                break;
            case RecoveredWorkKind.ReviewLoop:
                launchers.ReviewLoop.Launch(new ReviewAssignment(work.SpecRunId, work.TicketRunId!.Value, work.ResumeSessionId));
                break;
            case RecoveredWorkKind.Integration:
                launchers.Integration.Launch(new IntegrationAssignment(spec.RepositoryId, work.SpecRunId, work.TicketRunId!.Value));
                break;
            case RecoveredWorkKind.ParentReview:
                launchers.ParentReview.Launch(new ParentReviewAssignment(work.SpecRunId));
                break;
            case RecoveredWorkKind.Testing:
                launchers.Testing.Launch(new TestingAssignment(work.SpecRunId));
                break;
        }
    }
}
