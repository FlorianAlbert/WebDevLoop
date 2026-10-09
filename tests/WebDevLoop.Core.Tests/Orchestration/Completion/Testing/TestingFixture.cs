using WebDevLoop.Core.Agents;
using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Events;
using WebDevLoop.Core.Orchestration.Completion.ParentReview;
using WebDevLoop.Core.Orchestration.Completion.Testing;
using WebDevLoop.Core.Orchestration.Findings;
using WebDevLoop.Core.Orchestration.Frontier;
using WebDevLoop.Core.Orchestration.Results;
using WebDevLoop.Core.Ports;
using WebDevLoop.Core.Settings;
using WebDevLoop.Core.Tests.Orchestration.Completion.ParentReview;
using WebDevLoop.Core.Tests.Orchestration.Findings;
using WebDevLoop.Core.Tests.Orchestration.TicketExecution;

namespace WebDevLoop.Core.Tests.Orchestration.Completion.Testing;

/// <summary>
/// Runs the tester loop with the real finding issuer against a scripted tester agent, a scripted test-target supervisor,
/// in-memory Git, and an in-memory issue tracker. Every service call gets a fresh unit-of-work scope.
/// </summary>
internal sealed class TestingFixture
{
    public const string SkillsRoot = TicketExecutionFixture.SkillsRoot;

    private int _eventMark;

    public ParentReviewFixture Parent { get; } = new();

    public TestLeaseStore Leases { get; } = new();

    public ScriptedTestTarget Target { get; } = new();

    public TesterAgentStub Tester { get; } = new();

    public TestTestingLauncher Launcher { get; } = new();

    public TestingOptions Options { get; set; } = new(SkillsRoot);

    public TicketExecutionFixture Execution => Parent.Execution;

    public CasWorkflowDatabase Db => Parent.Db;

    public FindingIssueTracker Issues => Parent.Issues;

    public static CancellationToken Token => TicketExecutionFixture.Token;

    public TestingWorkflowScope OpenScope() => new(Parent.OpenScope(), Leases);

    /// <param name="git">Defaults to the shared in-memory Git.</param>
    public SpecTestRunner Runner(TestingWorkflowScope? scope = null, IGitWorkspace? git = null)
    {
        scope ??= OpenScope();
        git ??= Execution.Git;
        CasWorkflowScope workflow = scope.Workflow;
        var issuer = new FindingTicketIssuer(workflow, scope.Findings, Issues, scope, Execution.Ids, Execution.Clock);
        var attempts = new TesterAttemptRunner(
            workflow, scope, Tester, Target, git, new PromptRenderer(), workflow, scope, Execution.Ids, Execution.Clock, Options);
        return new SpecTestRunner(
            workflow, workflow, workflow, workflow, Execution.Settings, git, attempts, issuer, workflow, scope, Execution.Clock);
    }

    public Task<TestingResult> RunAsync(RunId specRunId, CancellationToken? cancellationToken = null) =>
        Runner().RunAsync(new TestingAssignment(specRunId), cancellationToken ?? Token);

    public TestingEventHandler Handler() => new(Launcher);

    /// <summary>A spec whose integrated ticket (#2) passed the parent-spec review: <c>Testing</c>, test cycle 1.</summary>
    public async Task<SeededSpec> SeedTestingAsync()
    {
        SeededSpec spec = await Parent.SeedParentReviewingAsync();
        await Parent.MoveSpecAsync(spec.Id, SpecRunStatus.Testing);
        MarkEvents();
        return spec;
    }

    public void MarkEvents()
    {
        Db.TakeUndispatchedEvents();
        _eventMark = Db.CommittedEvents.Count;
    }

    public void Configure(Func<EffectiveSettings, EffectiveSettings> change) => Execution.Settings.Defaults = change(Execution.Settings.Defaults);

    public void UseTesterTemplate(string template) =>
        Configure(settings => settings with
        {
            Roles = settings.Roles.ToDictionary(pair => pair.Key, pair => pair.Key == AgentRole.Tester ? pair.Value with { PromptTemplate = template } : pair.Value),
        });

    /// <summary>
    /// Outbox dispatcher: delivers committed events to the frontier (WP-14), parent-review, and testing handlers until no new
    /// events appear; launched parent reviews and tester runs are only recorded.
    /// </summary>
    public async Task PumpEventsAsync()
    {
        const int MaxRounds = 50;
        long messageId = 0;
        for (int round = 0; round < MaxRounds; round++)
        {
            IReadOnlyList<WorkflowEvent> pending = Db.TakeUndispatchedEvents();
            if (pending.Count == 0)
            {
                return;
            }

            foreach (WorkflowEvent workflowEvent in pending)
            {
                var envelope = new EventEnvelope(++messageId, workflowEvent);
                await new FrontierEventHandler(Execution.Frontier()).HandleAsync(envelope, Token);
                await Parent.Handler().HandleAsync(envelope, Token);
                await Handler().HandleAsync(envelope, Token);
            }
        }

        throw new InvalidOperationException("Event pumping did not settle.");
    }

    public SpecRun Spec(RunId id) => Execution.Spec(id);

    public IReadOnlyList<TicketRun> Tickets(RunId specRunId) => Parent.Tickets(specRunId);

    public IReadOnlyList<StepRun> TestSteps(RunId specRunId) =>
        Db.Rows<StepRun>().Where(step => step.SpecRunId == specRunId && step.Kind == StepKind.Test).OrderBy(step => step.Attempt).ToArray();

    /// <summary>Workflow events committed after seeding.</summary>
    public IEnumerable<WorkflowEvent> Events => Db.CommittedEvents.Skip(_eventMark);

    public IEnumerable<SpecRunStatusChanged> SpecTransitions(RunId specRunId) =>
        Events.OfType<SpecRunStatusChanged>().Where(changed => changed.SpecRunId == specRunId);

    public static TestReport Pass() =>
        new(TestVerdict.Pass, "Every requirement works in the browser.", [new TestScenario("Create a todo", TestScenarioOutcome.Passed)], []);

    public static TestReport IssuesFound(params TestIssue[] issues) =>
        new(TestVerdict.IssuesFound, "Saving fails.", [new TestScenario("Save an empty todo", TestScenarioOutcome.Failed)], issues);

    public static TestReport Blocked(string summary) => new(TestVerdict.Blocked, summary, [], []);

    public static TestIssue EmptyTitleCrash { get; } = new(
        "Saving a todo with an empty title shows a server error",
        TestIssueSeverity.Major,
        "Users can add todos with a title.",
        ["Open http://localhost:41000/", "Leave the title empty", "Click Save"],
        "A validation message asks for a title.",
        "The page shows 'HTTP 500'.",
        ["/work/runs/run/notes/test-evidence/attempt-1/empty-title.png"]);
}

internal sealed class TestTestingLauncher : ITestingLauncher
{
    public List<TestingAssignment> Launched { get; } = [];

    public void Launch(TestingAssignment assignment) => Launched.Add(assignment);
}
