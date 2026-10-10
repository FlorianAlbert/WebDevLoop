using WebDevLoop.Core.Agents;
using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Orchestration.Preparation;
using WebDevLoop.Core.Orchestration.Results;
using WebDevLoop.Core.Ports;
using WebDevLoop.Core.Tests.Orchestration.SpecQueue;

namespace WebDevLoop.Core.Tests.Orchestration.Preparation;

public sealed class SpecExplorationTests
{
    private const string ExplorerTemplate = "notes={exploration_notes_path} checkout={worktree_path} tip={integration_tip_sha}\n{spec_tickets}";

    private readonly SpecWorkflowFixture _fixture = new(explorationEnabled: true);

    private IStepRunRepository StepRuns => _fixture.Store;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private GitRepositoryLocation Location => GitRepositoryLocation.From(_fixture.Repository);

    public SpecExplorationTests()
    {
        _fixture.GlobalSettings.SetRole(AgentRole.Explorer, new RoleSettingsOverride(PromptTemplate: ExplorerTemplate));
        _fixture.SeedSpec(1);
        _fixture.SeedTicket(2, spec: 1);
        _fixture.SeedTicket(3, spec: 1, blockedByTickets: 2);
    }

    [Fact]
    public async Task explorer_gets_an_app_allocated_notes_path_outside_the_repo()
    {
        WorktreeInspection? checkoutSeenByExplorer = null;
        _fixture.Agents.Script(AgentRole.Explorer, request =>
        {
            checkoutSeenByExplorer = _fixture.Git.InspectWorktreeAsync(Location, request.Policy.Paths.WorkingDirectory, Ct).GetAwaiter().GetResult();
            return Completed(request);
        });
        SpecRun run = await ActivateAsync();

        PreparationOutcome outcome = await _fixture.PrepareAsync(run);

        Assert.Equal(PreparationOutcome.Prepared, outcome);
        AgentRunRequest request = Assert.Single(_fixture.Agents.Started);
        Assert.Equal(AgentRole.Explorer, request.Role);
        string notes = Assert.Single(request.Policy.Paths.WritableRoots);
        string checkout = request.Policy.Paths.WorkingDirectory;
        Assert.True(Path.IsPathFullyQualified(notes));
        Assert.StartsWith(SpecWorkflowFixture.WorkspaceRoot + "/", notes);
        Assert.Contains(run.Id.Value, notes);
        Assert.False(IsUnder(notes, SpecWorkflowFixture.RepositoryClonePath));
        Assert.False(IsUnder(notes, checkout));
        Assert.False(IsUnder(checkout, SpecWorkflowFixture.RepositoryClonePath));
        Assert.Equal([notes], _fixture.Directories.Created);
        Assert.Contains($"notes={notes} checkout={checkout} tip={_fixture.TrunkTip}", request.Prompt);
        Assert.Contains("#3 Ticket 3", request.Prompt);
        Assert.Contains("#2", request.Prompt.Split('\n').Single(line => line.Contains("#3 Ticket 3")));

        Assert.Equal(_fixture.TrunkTip, checkoutSeenByExplorer?.Head);
        Assert.Equal(WorktreeStatus.Missing, (await _fixture.Git.InspectWorktreeAsync(Location, checkout, Ct)).Status);

        StepRun step = Assert.Single(await StepRuns.ListBySpecRunAsync(run.Id, Ct));
        Assert.Equal((StepKind.Explore, AgentRole.Explorer, StepStatus.Succeeded, 1), (step.Kind, step.AgentRole, step.Status, step.Attempt));
        Assert.Equal(request.SessionId.Value, step.CopilotSessionId);
        Assert.Equal(request.StepRunId, step.Id);
        Assert.False(string.IsNullOrWhiteSpace(step.Model));
        Assert.Equal((request.Settings.Model, request.Settings.ReasoningEffort), (step.Model, step.ReasoningEffort));
        Assert.Contains("README.md", step.StructuredResultJson);
        Assert.Equal(SpecRunStatus.Running, run.Status);
    }

    [Fact]
    public async Task blocked_exploration_is_retried_with_a_new_attempt()
    {
        _fixture.Agents
            .Script(AgentRole.Explorer, _ => new ExplorationReport(ReportStatus.Blocked, "docs unreachable", []))
            .Script(AgentRole.Explorer, Completed);
        SpecRun run = await ActivateAsync();

        PreparationOutcome outcome = await _fixture.PrepareAsync(run);

        Assert.Equal(PreparationOutcome.Prepared, outcome);
        StepRun[] steps = (await StepRuns.ListBySpecRunAsync(run.Id, Ct)).OrderBy(step => step.Attempt).ToArray();
        Assert.Equal([(1, StepStatus.Failed), (2, StepStatus.Succeeded)], steps.Select(step => (step.Attempt, step.Status)));
        Assert.Contains("docs unreachable", steps[0].FailureReason);
        Assert.Equal(2, _fixture.Agents.Started.Select(request => request.SessionId).Distinct().Count());
    }

    [Fact]
    public async Task exploration_failing_every_attempt_needs_attention()
    {
        _fixture.GlobalSettings.MaxRetries = 1;
        SpecRun run = await ActivateAsync();

        PreparationOutcome outcome = await _fixture.PrepareAsync(run);

        Assert.Equal(PreparationOutcome.NeedsAttention, outcome);
        Assert.Equal(SpecRunStatus.NeedsAttention, run.Status);
        Assert.Contains("Exploration", run.FailureReason);
        Assert.Equal(2, _fixture.Agents.Started.Count);
        Assert.All(await StepRuns.ListBySpecRunAsync(run.Id, Ct), step => Assert.Equal(StepStatus.Failed, step.Status));
    }

    [Fact]
    public async Task successful_exploration_is_not_repeated_when_preparation_resumes()
    {
        _fixture.Agents.Script(AgentRole.Explorer, Completed);
        SpecRun run = await ActivateAsync();
        await _fixture.PrepareAsync(run);
        run.MarkNeedsAttention("parked for the test", _fixture.Clock.UtcNow);
        run.TransitionTo(SpecRunStatus.Preparing, _fixture.Clock.UtcNow);

        PreparationOutcome outcome = await _fixture.PrepareAsync(run);

        Assert.Equal(PreparationOutcome.Prepared, outcome);
        Assert.Single(_fixture.Agents.Started);
    }

    [Fact]
    public async Task disabled_exploration_never_starts_an_explorer()
    {
        var fixture = new SpecWorkflowFixture(explorationEnabled: false);
        fixture.SeedSpec(1);
        fixture.SeedTicket(2, spec: 1);
        SpecRun run = await fixture.EnqueueAsync(1);
        await fixture.ScheduleAsync();

        PreparationOutcome outcome = await fixture.PrepareAsync(run);

        Assert.Equal(PreparationOutcome.Prepared, outcome);
        Assert.Empty(fixture.Agents.Started);
        Assert.Empty(fixture.Directories.Created);
    }

    private static ExplorationReport Completed(AgentRunRequest request) =>
        new(ReportStatus.Completed, "mapped the code", [Path.Combine(request.Policy.Paths.WritableRoots[0], "README.md")]);

    private static bool IsUnder(string path, string root) =>
        path == root || path.StartsWith(root.TrimEnd('/') + "/", StringComparison.Ordinal);

    private async Task<SpecRun> ActivateAsync()
    {
        SpecRun run = await _fixture.EnqueueAsync(1);
        await _fixture.ScheduleAsync();
        return run;
    }
}
