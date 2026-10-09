using WebDevLoop.Core.Agents;
using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Events;
using WebDevLoop.Core.Orchestration.Completion.Testing;
using WebDevLoop.Core.Orchestration.Findings;
using WebDevLoop.Core.Orchestration.Results;
using WebDevLoop.Core.Ports;
using WebDevLoop.Core.Tests.Orchestration.Findings;
using WebDevLoop.Core.Tests.Orchestration.TicketExecution;

namespace WebDevLoop.Core.Tests.Orchestration.Completion.Testing;

public sealed class SpecTestRunnerTests
{
    private static readonly TestIssue EmptyTitleCrash = TestingFixture.EmptyTitleCrash;

    private readonly TestingFixture _fixture = new();

    [Fact]
    public async Task Reserved_port_appears_in_tester_prompt_and_environment()
    {
        _fixture.UseTesterTemplate("{reserved_port} {app_url} [{tester_instructions}] {worktree_path} {branch_name}");
        _fixture.Configure(settings => settings with { TesterRunInstructions = "npm start -- --port $PORT", TestPortRange = new TestPortRange(41007, 41009) });
        SeededSpec spec = await _fixture.SeedTestingAsync();
        TestWorkspace workspace = TestWorkspace.For(TicketExecutionFixture.WorkspaceRoot, spec.Id);
        TestLease? leaseDuringTurn = null;
        StepStatus? stepDuringTurn = null;
        _fixture.Tester.DuringTurn = _ =>
        {
            leaseDuringTurn = Assert.Single(_fixture.Leases.Rows, lease => lease.IsActive);
            stepDuringTurn = Assert.Single(_fixture.TestSteps(spec.Id)).Status;
        };
        _fixture.Tester.Reports(TestingFixture.Pass());

        TestingResult result = await _fixture.RunAsync(spec.Id);

        Assert.Equal(TestingOutcome.Passed, result.Outcome);
        AgentRunRequest request = Assert.Single(_fixture.Tester.Started);
        Assert.Equal($"41007 http://localhost:41007/ [npm start -- --port $PORT] {workspace.CheckoutDirectory} webdevloop/{spec.Id}/test", request.Prompt);
        Assert.Equal(AgentRole.Tester, request.Role);
        Assert.Equal(workspace.CheckoutDirectory, request.Policy.Paths.WorkingDirectory);
        IReadOnlyDictionary<string, string> shell = request.Policy.BuildEnvironment(new Dictionary<string, string>());
        Assert.Equal("41007", shell[ScriptedTestTarget.PortVariable]);
        Assert.Equal("http://localhost:41007/", shell[ScriptedTestTarget.AppUrlVariable]);
        Assert.Equal((41007, workspace.CheckoutDirectory), (leaseDuringTurn!.Port, leaseDuringTurn.WorkspacePath));
        Assert.Equal(StepStatus.Running, stepDuringTurn);
        Assert.False(Assert.Single(_fixture.Leases.Rows).IsActive);
        Assert.Equal(41007, Assert.Single(_fixture.Target.Stopped).Target.Port);
    }

    [Fact]
    public async Task Passing_tester_keeps_spec_testing_and_hands_it_to_completion()
    {
        SeededSpec spec = await _fixture.SeedTestingAsync();
        string checkout = TestWorkspace.For(TicketExecutionFixture.WorkspaceRoot, spec.Id).CheckoutDirectory;
        _fixture.Tester.Reports(TestingFixture.Pass());

        TestingResult result = await _fixture.RunAsync(spec.Id);

        Assert.Equal(TestingOutcome.Passed, result.Outcome);
        Assert.Equal(SpecRunStatus.Testing, _fixture.Spec(spec.Id).Status);
        Assert.Empty(_fixture.SpecTransitions(spec.Id));
        SpecTestingPassed passed = Assert.Single(_fixture.Events.OfType<SpecTestingPassed>());
        Assert.Equal((spec.Id, spec.RepositoryId, 1), (passed.SpecRunId, passed.RepositoryId, passed.TestCycle));
        StepRun step = Assert.Single(_fixture.TestSteps(spec.Id));
        Assert.Equal((StepStatus.Succeeded, AgentRole.Tester, (TicketRunId?)null), (step.Status, step.AgentRole, step.TicketRunId));
        Assert.Equal(("default-model", "medium"), (step.Model, step.ReasoningEffort));
        Assert.Contains("\"verdict\":\"pass\"", step.StructuredResultJson, StringComparison.Ordinal);
        Assert.Equal(WorktreeStatus.Missing, (await _fixture.Execution.Git.InspectWorktreeAsync(spec.Location, checkout, TestingFixture.Token)).Status);
        Assert.Empty(_fixture.Issues.CreatedDrafts);
    }

    [Fact]
    public async Task Readiness_timeout_fails_the_test_step_aborts_the_tester_and_stops_the_lease()
    {
        _fixture.Configure(settings => settings with { MaxRetries = 0 });
        _fixture.Options = _fixture.Options with { AppStartupTimeout = TimeSpan.FromMinutes(3) };
        SeededSpec spec = await _fixture.SeedTestingAsync();
        _fixture.Target.Startup(FakeStartup.TimedOut);
        _fixture.Tester.Hangs();

        TestingResult result = await _fixture.RunAsync(spec.Id);

        Assert.Equal(TestingOutcome.Failed, result.Outcome);
        Assert.Equal([TimeSpan.FromMinutes(3)], _fixture.Target.ReadinessTimeouts);
        StepRun step = Assert.Single(_fixture.TestSteps(spec.Id));
        Assert.Equal(StepStatus.Failed, step.Status);
        Assert.Contains("41000", step.FailureReason, StringComparison.Ordinal);
        Assert.Equal([new AgentSessionId(step.CopilotSessionId!)], _fixture.Tester.Aborted);
        Assert.Single(_fixture.Target.Stopped);
        Assert.False(Assert.Single(_fixture.Leases.Rows).IsActive);
        SpecRun run = _fixture.Spec(spec.Id);
        Assert.Equal(SpecRunStatus.NeedsAttention, run.Status);
        Assert.Contains(step.FailureReason!, run.FailureReason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Startup_failure_fails_the_test_step_stops_the_lease_and_retries_in_a_fresh_session()
    {
        _fixture.Configure(settings => settings with { MaxRetries = 1 });
        SeededSpec spec = await _fixture.SeedTestingAsync();
        _fixture.Target.Startup(FakeStartup.Crashes, FakeStartup.Ready);
        _fixture.Tester.Hangs().Reports(TestingFixture.Pass());

        TestingResult result = await _fixture.RunAsync(spec.Id);

        Assert.Equal(TestingOutcome.Passed, result.Outcome);
        StepRun[] steps = _fixture.TestSteps(spec.Id).ToArray();
        Assert.Equal([StepStatus.Failed, StepStatus.Succeeded], steps.Select(step => step.Status));
        Assert.Equal([1, 2], steps.Select(step => step.Attempt));
        Assert.Contains("exited with code 134", steps[0].FailureReason, StringComparison.Ordinal);
        Assert.NotEqual(steps[0].CopilotSessionId, steps[1].CopilotSessionId);
        Assert.Equal(2, _fixture.Target.Stopped.Count);
        Assert.Equal(2, _fixture.Leases.Rows.Count(lease => !lease.IsActive));
    }

    [Fact]
    public async Task Pass_without_the_application_ever_accepting_connections_fails_the_step()
    {
        _fixture.Configure(settings => settings with { MaxRetries = 0 });
        SeededSpec spec = await _fixture.SeedTestingAsync();
        _fixture.Target.Startup(FakeStartup.Pending);
        _fixture.Tester.Reports(TestingFixture.Pass());

        TestingResult result = await _fixture.RunAsync(spec.Id);

        Assert.Equal(TestingOutcome.Failed, result.Outcome);
        StepRun step = Assert.Single(_fixture.TestSteps(spec.Id));
        Assert.Equal(StepStatus.Failed, step.Status);
        Assert.Contains("never accepted connections", step.FailureReason, StringComparison.Ordinal);
        Assert.Empty(_fixture.Events.OfType<SpecTestingPassed>());
        Assert.Equal(SpecRunStatus.NeedsAttention, _fixture.Spec(spec.Id).Status);
    }

    [Fact]
    public async Task Tester_exception_finishes_the_step_as_failed_instead_of_leaving_it_running()
    {
        _fixture.Configure(settings => settings with { MaxRetries = 0 });
        SeededSpec spec = await _fixture.SeedTestingAsync();
        _fixture.Tester.Throws(new InvalidOperationException("Copilot runtime crashed."));

        TestingResult result = await _fixture.RunAsync(spec.Id);

        Assert.Equal(TestingOutcome.Failed, result.Outcome);
        StepRun step = Assert.Single(_fixture.TestSteps(spec.Id));
        Assert.Equal(StepStatus.Failed, step.Status);
        Assert.Contains("Copilot runtime crashed.", step.FailureReason, StringComparison.Ordinal);
        Assert.Single(_fixture.Target.Stopped);
        Assert.False(Assert.Single(_fixture.Leases.Rows).IsActive);
        Assert.Equal(SpecRunStatus.NeedsAttention, _fixture.Spec(spec.Id).Status);
    }

    [Fact]
    public async Task Tester_finding_creates_a_finding_ticket_and_returns_spec_to_running()
    {
        SeededSpec spec = await _fixture.SeedTestingAsync();
        _fixture.Tester.Reports(TestingFixture.IssuesFound(EmptyTitleCrash));

        TestingResult result = await _fixture.RunAsync(spec.Id);

        Assert.Equal(TestingOutcome.FindingTicketsCreated, result.Outcome);
        FindingIssueDraft draft = Assert.Single(_fixture.Issues.CreatedDrafts);
        Assert.Equal(FindingFingerprints.Compute(spec.Id, StepKind.Test, EmptyTitleCrash), draft.Fingerprint);
        FindingTicket ticket = Assert.Single(result.Tickets);
        Assert.Equal([ticket.Issue.Number], _fixture.Issues.SubIssueNumbers(_fixture.Spec(spec.Id).ParentIssue));
        Assert.Equal(TicketRunStatus.Blocked, Assert.Single(_fixture.Tickets(spec.Id), run => run.Id == ticket.TicketRunId).Status);
        StepRun step = Assert.Single(_fixture.TestSteps(spec.Id));
        FindingIssuance issuance = Assert.Single(_fixture.Parent.Issuances.Rows);
        Assert.Equal((step.Id, FindingAxis.Testing), (issuance.SourceStepRunId, issuance.Axis));
        Assert.Equal(SpecRunStatus.Running, _fixture.Spec(spec.Id).Status);
        SpecRunStatusChanged changed = Assert.Single(_fixture.SpecTransitions(spec.Id));
        Assert.Equal((SpecRunStatus.Testing, SpecRunStatus.Running), (changed.From, changed.To));
        Assert.Empty(_fixture.Events.OfType<SpecTestingPassed>());
    }

    [Fact]
    public async Task Repeated_tester_finding_reuses_its_done_ticket_and_needs_attention()
    {
        SeededSpec spec = await _fixture.SeedTestingAsync();
        _fixture.Tester.Reports(TestingFixture.IssuesFound(EmptyTitleCrash)).Reports(TestingFixture.IssuesFound(EmptyTitleCrash));
        FindingTicket first = Assert.Single((await _fixture.RunAsync(spec.Id)).Tickets);
        await _fixture.Parent.WorkTicketAsync(spec, first.TicketRunId);
        await _fixture.Parent.MoveSpecAsync(spec.Id, SpecRunStatus.ParentReviewing);
        await _fixture.Parent.MoveSpecAsync(spec.Id, SpecRunStatus.Testing);

        TestingResult result = await _fixture.RunAsync(spec.Id);

        Assert.Equal(TestingOutcome.NoNewWork, result.Outcome);
        FindingTicket reused = Assert.Single(result.Tickets);
        Assert.Equal((first.TicketRunId, FindingTicketOrigin.ExistingTicket), (reused.TicketRunId, reused.Origin));
        Assert.Single(_fixture.Issues.CreatedDrafts);
        SpecRun run = _fixture.Spec(spec.Id);
        Assert.Equal((SpecRunStatus.NeedsAttention, 2), (run.Status, run.TestCycle));
        Assert.Contains($"#{first.Issue.Number}", run.FailureReason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Tester_cycle_limit_creates_the_tickets_and_moves_spec_to_needs_attention()
    {
        _fixture.Configure(settings => settings with { TesterCycleLimit = 1 });
        SeededSpec spec = await _fixture.SeedTestingAsync();
        _fixture.Tester.Reports(TestingFixture.IssuesFound(EmptyTitleCrash));

        TestingResult result = await _fixture.RunAsync(spec.Id);

        Assert.Equal(TestingOutcome.CycleLimitReached, result.Outcome);
        Assert.Single(result.Tickets);
        Assert.Single(_fixture.Issues.CreatedDrafts);
        SpecRun run = _fixture.Spec(spec.Id);
        Assert.Equal(SpecRunStatus.NeedsAttention, run.Status);
        Assert.Contains("limit is 1", run.FailureReason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Blocked_tester_moves_spec_to_needs_attention_with_its_explanation()
    {
        SeededSpec spec = await _fixture.SeedTestingAsync();
        _fixture.Tester.Reports(TestingFixture.Blocked("The run instructions need a database that is not installed."));

        TestingResult result = await _fixture.RunAsync(spec.Id);

        Assert.Equal(TestingOutcome.Blocked, result.Outcome);
        SpecRun run = _fixture.Spec(spec.Id);
        Assert.Equal(SpecRunStatus.NeedsAttention, run.Status);
        Assert.Contains("database that is not installed", run.FailureReason, StringComparison.Ordinal);
        Assert.Equal(StepStatus.Succeeded, Assert.Single(_fixture.TestSteps(spec.Id)).Status);
        Assert.Empty(_fixture.Issues.CreatedDrafts);
    }

    [Fact]
    public async Task Tester_timeout_kills_leftover_processes_and_retries()
    {
        SeededSpec spec = await _fixture.SeedTestingAsync();
        _fixture.Target.LeftoverProcesses = 3;
        _fixture.Tester.Ends(AgentRunOutcome.TimedOut, "The tester did not finish within 60 minutes.").Reports(TestingFixture.Pass());

        TestingResult result = await _fixture.RunAsync(spec.Id);

        Assert.Equal(TestingOutcome.Passed, result.Outcome);
        Assert.Equal([StepStatus.TimedOut, StepStatus.Succeeded], _fixture.TestSteps(spec.Id).Select(step => step.Status));
        Assert.Equal([3, 0], _fixture.Target.Stopped.Select(stop => stop.Killed));
        Assert.All(_fixture.Leases.Rows, lease => Assert.False(lease.IsActive));
    }

    [Fact]
    public async Task Aborting_the_run_cancels_the_tester_and_kills_leftover_processes()
    {
        SeededSpec spec = await _fixture.SeedTestingAsync();
        using var abort = new CancellationTokenSource();
        _fixture.Target.LeftoverProcesses = 2;
        _fixture.Tester.DuringTurn = _ => abort.CancelAfter(TimeSpan.FromMilliseconds(20));
        _fixture.Tester.Hangs();

        TestingResult result = await _fixture.RunAsync(spec.Id, abort.Token);

        Assert.Equal(TestingOutcome.Cancelled, result.Outcome);
        Assert.Equal(StepStatus.Cancelled, Assert.Single(_fixture.TestSteps(spec.Id)).Status);
        Assert.Equal(2, Assert.Single(_fixture.Target.Stopped).Killed);
        Assert.False(Assert.Single(_fixture.Leases.Rows).IsActive);
        Assert.Equal(SpecRunStatus.Testing, _fixture.Spec(spec.Id).Status);
    }

    [Fact]
    public async Task Tester_role_cannot_mutate_github_or_push()
    {
        SeededSpec spec = await _fixture.SeedTestingAsync();
        _fixture.Target.ExtraEnvironment["GH_TOKEN"] = "ghs_injected";
        _fixture.Target.ExtraEnvironment["GIT_TERMINAL_PROMPT"] = "1";
        CommitSha? remoteTip = _fixture.Execution.Git.RemoteTip(spec.IntegrationBranch);
        _fixture.Tester.Reports(TestingFixture.Pass());

        await _fixture.RunAsync(spec.Id);

        RoleCapabilityPolicy policy = Assert.Single(_fixture.Tester.Started).Policy;
        Assert.Equal(GitHubTokenAccess.None, policy.TokenAccess);
        Assert.False(policy.RequiresGitHubWriteToken);
        Assert.Equal(AgentReportTools.Test, policy.ReportToolName);
        Assert.All(
            ["git push origin HEAD", "gh issue create --title x", "gh pr create", "gh stack push", "git commit -am fix", "git remote add evil x"],
            command => Assert.False(policy.IsCommandAllowed(command), command));
        IReadOnlyDictionary<string, string> shell = policy.BuildEnvironment(new Dictionary<string, string> { ["GITHUB_TOKEN"] = "ghp_parent" });
        Assert.DoesNotContain("GH_TOKEN", shell.Keys);
        Assert.DoesNotContain("GITHUB_TOKEN", shell.Keys);
        Assert.Equal("0", shell["GIT_TERMINAL_PROMPT"]);
        Assert.Equal(remoteTip, _fixture.Execution.Git.RemoteTip(spec.IntegrationBranch));
        Assert.Empty(_fixture.Issues.CreatedDrafts);
    }

    [Fact]
    public async Task No_free_port_moves_spec_to_needs_attention_without_starting_a_tester()
    {
        SeededSpec spec = await _fixture.SeedTestingAsync();
        _fixture.Target.NoFreePort = true;

        TestingResult result = await _fixture.RunAsync(spec.Id);

        Assert.Equal(TestingOutcome.Failed, result.Outcome);
        Assert.Empty(_fixture.Tester.Started);
        Assert.Empty(_fixture.TestSteps(spec.Id));
        Assert.Empty(_fixture.Leases.Rows);
        SpecRun run = _fixture.Spec(spec.Id);
        Assert.Equal(SpecRunStatus.NeedsAttention, run.Status);
        Assert.Contains("41000-41999", run.FailureReason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Invalid_tester_prompt_frees_the_reserved_port_and_moves_spec_to_needs_attention()
    {
        _fixture.UseTesterTemplate("Test on {unknown_port}.");
        SeededSpec spec = await _fixture.SeedTestingAsync();

        TestingResult result = await _fixture.RunAsync(spec.Id);

        Assert.Equal(TestingOutcome.Failed, result.Outcome);
        Assert.Empty(_fixture.Tester.Started);
        Assert.Empty(_fixture.TestSteps(spec.Id));
        Assert.Empty(_fixture.Leases.Rows);
        Assert.Equal(41000, Assert.Single(_fixture.Target.Stopped).Target.Port);
        SpecRun run = _fixture.Spec(spec.Id);
        Assert.Equal(SpecRunStatus.NeedsAttention, run.Status);
        Assert.Contains("prompt cannot be rendered", run.FailureReason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Restart_after_the_tester_reported_reuses_its_verdict_instead_of_testing_again()
    {
        SeededSpec spec = await _fixture.SeedTestingAsync();
        _fixture.Tester.Reports(TestingFixture.IssuesFound(EmptyTitleCrash));
        _fixture.Issues.CrashAfterNextCreate = true;
        await Assert.ThrowsAsync<SimulatedCrashException>(() => _fixture.RunAsync(spec.Id));
        Assert.Equal(SpecRunStatus.Testing, _fixture.Spec(spec.Id).Status);

        TestingResult result = await _fixture.RunAsync(spec.Id);

        Assert.Equal(TestingOutcome.FindingTicketsCreated, result.Outcome);
        Assert.Single(_fixture.Tester.Started);
        Assert.Single(_fixture.Issues.CreatedDrafts);
        Assert.Single(_fixture.Tickets(spec.Id), ticket => ticket.Id == Assert.Single(result.Tickets).TicketRunId);
        Assert.Equal(SpecRunStatus.Running, _fixture.Spec(spec.Id).Status);
    }

    [Fact]
    public async Task Restart_reusing_the_persisted_verdict_keeps_the_reported_blocking_relations()
    {
        SeededSpec spec = await _fixture.SeedTestingAsync();
        TestIssue validation = Issue("Empty titles are accepted", id: "validation");
        TestIssue message = Issue("No error message for empty titles", blockedBy: ["validation"]);
        _fixture.Tester.Reports(TestingFixture.IssuesFound(validation, message));
        _fixture.Issues.CrashAfterNextCreate = true;
        await Assert.ThrowsAsync<SimulatedCrashException>(() => _fixture.RunAsync(spec.Id));

        TestingResult result = await _fixture.RunAsync(spec.Id);

        Assert.Equal(TestingOutcome.FindingTicketsCreated, result.Outcome);
        Assert.Single(_fixture.Tester.Started);
        (TicketRunId blocking, TicketRunId blocked) = (result.Tickets[0].TicketRunId, result.Tickets[1].TicketRunId);
        TicketDependency dependency = Assert.Single(_fixture.Db.TicketDependencies);
        Assert.Equal((blocked, blocking), (dependency.BlockedTicketRunId, dependency.BlockingTicketRunId));
    }

    private static TestIssue Issue(string title, string? id = null, IReadOnlyList<string>? blockedBy = null) =>
        new(title, TestIssueSeverity.Major, null, ["Open the app", "Save an empty todo"], "A validation message.", "It saves.", [], id, blockedBy);

    [Fact]
    public async Task Spec_that_is_not_testing_is_left_alone()
    {
        SeededSpec spec = await _fixture.Parent.SeedIntegratedSpecAsync();

        TestingResult result = await _fixture.RunAsync(spec.Id);

        Assert.Equal(TestingOutcome.NotTesting, result.Outcome);
        Assert.Empty(_fixture.Target.Reserved);
        Assert.Equal(SpecRunStatus.Running, _fixture.Spec(spec.Id).Status);
    }

    [Fact]
    public async Task Second_runner_for_a_spec_being_tested_starts_nothing()
    {
        SeededSpec spec = await _fixture.SeedTestingAsync();
        TestingResult? concurrent = null;
        _fixture.Tester.DuringTurn = _ => concurrent = _fixture.RunAsync(spec.Id).GetAwaiter().GetResult();
        _fixture.Tester.Reports(TestingFixture.Pass());

        TestingResult result = await _fixture.RunAsync(spec.Id);

        Assert.Equal(TestingOutcome.Passed, result.Outcome);
        Assert.Equal(TestingOutcome.AlreadyRunning, concurrent!.Outcome);
        Assert.Single(_fixture.Tester.Started);
        Assert.Single(_fixture.Target.Reserved);
    }
}
