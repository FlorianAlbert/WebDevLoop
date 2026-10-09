using WebDevLoop.Core.Orchestration.Completion.ParentReview;
using WebDevLoop.Core.Orchestration.Completion.ReadyAndMerge;
using WebDevLoop.Core.Orchestration.Completion.Testing;
using WebDevLoop.Core.Orchestration.Integration;
using WebDevLoop.Core.Orchestration.Preparation;
using WebDevLoop.Core.Orchestration.ReviewLoop;
using WebDevLoop.Core.Orchestration.TicketExecution;

namespace WebDevLoop.Web.Background;

/// <summary>
/// The background launchers of every workflow phase: each launch resolves the phase's runner in a fresh DI scope (its own
/// unit of work) on the <see cref="BackgroundWorkRunner"/>, keyed by phase and ticket or spec so a phase never runs twice
/// concurrently for the same work.
/// </summary>
public sealed class BackgroundWorkflowLaunchers(BackgroundWorkRunner runner) :
    IPreparationLauncher,
    IImplementationLauncher,
    IReviewLoopLauncher,
    IIntegrationLauncher,
    IParentReviewLauncher,
    ITestingLauncher,
    ICompletionLauncher
{
    public void Launch(PreparationAssignment assignment) =>
        Run<SpecPreparationService>($"prepare:{assignment.SpecRunId}", (preparation, token) => preparation.PrepareAsync(assignment.SpecRunId, token));

    public void Launch(ImplementationAssignment assignment) =>
        Run<TicketImplementationRunner>($"implement:{assignment.TicketRunId}", (implementer, token) => implementer.RunAsync(assignment, token));

    public void Launch(ReviewAssignment assignment) =>
        Run<TicketReviewLoop>($"review:{assignment.TicketRunId}", (loop, token) => loop.RunAsync(assignment, token));

    public void Launch(IntegrationAssignment assignment) =>
        Run<IntegrationSagaRunner>($"integrate:{assignment.TicketRunId}", (saga, token) => saga.RunAsync(assignment, token));

    public void Launch(ParentReviewAssignment assignment) =>
        Run<ParentSpecReviewRunner>($"parent-review:{assignment.SpecRunId}", (review, token) => review.RunAsync(assignment, token));

    public void Launch(TestingAssignment assignment) =>
        Run<SpecTestRunner>($"test:{assignment.SpecRunId}", (tester, token) => tester.RunAsync(assignment, token));

    public void Launch(CompletionAssignment assignment) =>
        Run<SpecCompletionService>($"complete:{assignment.SpecRunId}", (completion, token) => completion.RunAsync(assignment, token));

    private void Run<TRunner>(string key, Func<TRunner, CancellationToken, Task> work)
        where TRunner : notnull =>
        runner.Run(key, (services, token) => work(services.GetRequiredService<TRunner>(), token));
}
