using WebDevLoop.Core.Agents;
using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Events;
using WebDevLoop.Core.Orchestration.ReviewLoop;
using WebDevLoop.Core.Orchestration.Results;
using WebDevLoop.Core.Orchestration.TicketExecution;
using WebDevLoop.Core.Ports;
using WebDevLoop.Core.Settings;
using WebDevLoop.Core.Tests.Orchestration.TicketExecution;
using WebDevLoop.Core.Tests.Ports.Fakes;

namespace WebDevLoop.Core.Tests.Orchestration.ReviewLoop;

/// <summary>
/// Drives tickets through the real WP-14 implementation runner into <c>Reviewing</c>, then runs the review loop against
/// scripted reviewer/implementer turns. Every service call gets a fresh unit-of-work scope over the shared CAS database.
/// </summary>
internal sealed class ReviewLoopFixture
{
    public const string SkillsRoot = TicketExecutionFixture.SkillsRoot;

    public TicketExecutionFixture Execution { get; } = new();

    public ScriptedAgentRunner Agents { get; } = new();

    public TestReviewLoopLauncher Launcher { get; } = new();

    public CasWorkflowDatabase Db => Execution.Db;

    public InMemoryGitWorkspace Git => Execution.Git;

    public FixedSettingsProvider Settings => Execution.Settings;

    public ImplementerCapacityGate Gate => Execution.Gate;

    public static CancellationToken Token => TicketExecutionFixture.Token;

    public static CodingStandardsFinding StandardsFinding { get; } = new(
        CodingStandardsSeverity.Blocking, "src/Feature.cs", 12, "var delay = 42;", "CONTRIBUTING.md: no magic values", "Magic number 42 in Feature.", "Name the constant.");

    public static SpecificationFinding SpecFinding { get; } = new(
        SpecificationFindingKind.Missing, "Errors must be logged.", "src/Feature.cs", null, "Errors are not logged.", "Log errors through ILogger.");

    public TwoAxisReviewRunner Reviews(CasWorkflowScope? scope = null)
    {
        scope ??= Db.OpenScope();
        return new TwoAxisReviewRunner(
            scope, scope, scope, scope, Settings, Git, Agents, new PromptRenderer(), scope, scope, Execution.Ids, Execution.Clock, new ReviewOptions(SkillsRoot));
    }

    public TicketReviewLoop Loop(CasWorkflowScope? scope = null)
    {
        scope ??= Db.OpenScope();
        var fixes = new ReviewFixRunner(
            scope,
            scope,
            Git,
            Agents,
            new PromptRenderer(),
            new ImplementerCapacity(scope),
            Gate,
            scope,
            scope,
            Execution.Ids,
            Execution.Clock,
            new TicketExecutionOptions(SkillsRoot));
        return new TicketReviewLoop(scope, scope, scope, scope, Settings, Git, Reviews(scope), fixes, scope, scope, Execution.Clock);
    }

    public ReviewLoopEventHandler Handler()
    {
        CasWorkflowScope scope = Db.OpenScope();
        return new ReviewLoopEventHandler(Launcher, scope, scope, scope);
    }

    public Task<ReviewLoopResult> RunLoopAsync(SeededSpec spec, int ticketNumber) =>
        Loop().RunAsync(new ReviewAssignment(spec.Id, spec[ticketNumber]), Token);

    /// <summary>Seeds a running spec and implements the given independent tickets with the real implementation runner.</summary>
    public async Task<SeededSpec> SeedReviewingAsync(params int[] ticketNumbers)
    {
        SeededSpec spec = await Execution.SeedRunningSpecAsync("app", [.. ticketNumbers.Select(number => (number, Array.Empty<int>()))]);
        Execution.UseRunner();
        Execution.Agents.AutoReply = request => TicketExecutionFixture.Completed(Execution.CommitInWorktree(request, $"feature-{request.StepRunId}.cs"));
        await Execution.ReconcileAsync(spec.Id);
        await Execution.Launcher.WhenAllFinishedAsync();
        Assert.All(ticketNumbers, number => Assert.Equal(TicketRunStatus.Reviewing, Ticket(spec[number]).Status));
        return spec;
    }

    public void UseTemplate(AgentRole role, string template) =>
        Settings.Defaults = Settings.Defaults with
        {
            Roles = Settings.Defaults.Roles.ToDictionary(pair => pair.Key, pair => pair.Key == role ? pair.Value with { PromptTemplate = template } : pair.Value),
        };

    public ReviewLoopFixture Clean(FindingAxis axis) =>
        Script(axis, _ => ReviewReport.Clean(axis, $"{axis} is clean."));

    public ReviewLoopFixture Issues(FindingAxis axis, params Finding[] findings) =>
        Script(axis, _ => ReviewReport.IssuesFound(axis, $"{axis} found issues.", findings));

    /// <summary>An implementer fix turn that commits in its worktree and reports the new head.</summary>
    public ReviewLoopFixture Fix(string file = "fix.cs")
    {
        Agents.Script(AgentRole.Implementer, request =>
            ImplementationReport.Completed(Git.CommitInWorktree(request.Policy.Paths.WorkingDirectory, file), "Fixed the findings."));
        return this;
    }

    public ReviewLoopFixture Script(FindingAxis axis, Func<AgentRunRequest, AgentReport> turn)
    {
        Agents.Script(ReviewAxes.ReviewerFor(axis), turn);
        return this;
    }

    /// <summary>Delivers all committed, not yet delivered events to the review loop event handler.</summary>
    public async Task DeliverEventsAsync()
    {
        long messageId = 0;
        foreach (WorkflowEvent workflowEvent in Db.TakeUndispatchedEvents())
        {
            await Handler().HandleAsync(new EventEnvelope(++messageId, workflowEvent), Token);
        }
    }

    public TicketRun Ticket(TicketRunId id) => Execution.Ticket(id);

    public SpecRun Spec(RunId id) => Execution.Spec(id);

    public IReadOnlyList<StepRun> Steps(TicketRunId id, StepKind kind) =>
        Execution.Steps(id).Where(step => step.Kind == kind).OrderBy(step => step.AgentRole).ThenBy(step => step.Attempt).ToArray();

    public IEnumerable<AgentRunRequest> ReviewerRequests =>
        Agents.Started.Where(request => request.Role is AgentRole.ReviewerCodingStandards or AgentRole.ReviewerSpecification);

    public IEnumerable<TicketRunStatusChanged> TicketTransitions(TicketRunId id) =>
        Db.CommittedEvents.OfType<TicketRunStatusChanged>().Where(changed => changed.TicketRunId == id);
}
