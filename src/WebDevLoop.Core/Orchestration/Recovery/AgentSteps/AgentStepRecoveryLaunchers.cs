using WebDevLoop.Core.Orchestration.Completion.ParentReview;
using WebDevLoop.Core.Orchestration.Completion.Testing;
using WebDevLoop.Core.Orchestration.Integration;
using WebDevLoop.Core.Orchestration.ReviewLoop;
using WebDevLoop.Core.Orchestration.TicketExecution;

namespace WebDevLoop.Core.Orchestration.Recovery.AgentSteps;

/// <summary>The background launchers recovered work is handed to; each is safe to launch twice.</summary>
public sealed record AgentStepRecoveryLaunchers(
    IImplementationLauncher Implementation,
    IReviewLoopLauncher ReviewLoop,
    IIntegrationLauncher Integration,
    IParentReviewLauncher ParentReview,
    ITestingLauncher Testing);
