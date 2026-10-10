using System.Text.Json;
using WebDevLoop.Core.Agents;
using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Orchestration.Attention;
using WebDevLoop.Core.Orchestration.Control;
using WebDevLoop.Core.Orchestration.Results;
using WebDevLoop.Core.Ports;
using WebDevLoop.Core.Queries;
using WebDevLoop.Core.Tests.Domain.Attention;
using WebDevLoop.Core.Tests.Orchestration.Control;

namespace WebDevLoop.Core.Tests.Orchestration.Attention;

public sealed class TroubleshooterStageTests : IDisposable
{
    private readonly AttentionFixture _f = new();
    private readonly SpecRun _spec;
    private readonly IAttentionStage _stage;

    public TroubleshooterStageTests()
    {
        _spec = _f.SeedRunningSpec();
        _stage = _f.Troubleshooter();
    }

    public void Dispose() => _f.Dispose();

    // ----- Stage ordering --------------------------------------------------------------------------------------

    [Fact]
    public async Task known_remediation_goes_first_and_a_fix_it_finds_means_no_agent_session()
    {
        (TicketRun ticket, _) = Park(AttentionReasons.WorktreeNotClean("w", "b", "dirty"), WorktreeStatus.Dirty);
        _f.Git.SetWorktreeChanges(_f.TicketPath(_spec, ticket), new WorktreeChanges(string.Empty, [], ["__pycache__/x.pyc"], []));

        AttentionTriageOutcome outcome = await Triage(ticket);

        Assert.Equal(AttentionTriageOutcome.Resolved, outcome);
        Assert.Empty(_f.Agents.Started);
    }

    [Fact]
    public async Task the_troubleshooter_runs_after_the_known_remediation_failed_and_before_the_user_is_asked()
    {
        (TicketRun ticket, string path) = Park(AttentionReasons.WorktreeNotClean("w", "b", "dirty"), WorktreeStatus.Locked);
        _f.Agents.Script(AgentRole.Troubleshooter, _ => Report(TroubleshooterOutcome.NeedsUser, userSteps: ["Unlock it."]));

        AttentionTriageOutcome outcome = await Triage(ticket);

        Assert.Equal(AttentionTriageOutcome.NeedsUser, outcome);
        AgentRunRequest request = Assert.Single(_f.Agents.Started);
        Assert.Equal(AgentRole.Troubleshooter, request.Role);
        string[] types = [.. _f.EventsOf(_spec).Select(runEvent => runEvent.Type)];
        Assert.True(Array.IndexOf(types, AttentionRunEvents.RemediationAttempted) < Array.IndexOf(types, TroubleshooterRunEvents.Started));
        Assert.True(Array.IndexOf(types, TroubleshooterRunEvents.Finished) < Array.IndexOf(types, AttentionRunEvents.NeedsYou));
        Assert.Equal(path, request.Policy.Paths.WorkingDirectory);
    }

    // ----- Not every reason escalates ------------------------------------------------------------------------------

    [Theory]
    [InlineData(AttentionCode.PromptNotRenderable)]
    [InlineData(AttentionCode.ReviewIterationsExhausted)]
    [InlineData(AttentionCode.IntegrationBranchMoved)]
    [InlineData(AttentionCode.StackBranchExists)]
    [InlineData(AttentionCode.IntegrationTemporaryFailure)]
    [InlineData(AttentionCode.TicketHasNoChanges)]
    [InlineData(AttentionCode.PullRequestNotOpen)]
    [InlineData(AttentionCode.InternalInconsistency)]
    public async Task reasons_that_are_deterministic_or_belong_to_the_user_never_start_a_session(AttentionCode code)
    {
        (TicketRun ticket, _) = Park(AttentionSamples.For(code), WorktreeStatus.Clean);

        AttentionStageResult result = await _stage.TryAsync(Case(ticket), AttentionFixture.Token);

        Assert.Equal(AttentionStageStatus.NotApplicable, result.Status);
        Assert.Empty(_f.Agents.Started);
    }

    [Fact]
    public async Task a_problem_of_the_whole_run_has_no_ticket_worktree_and_is_not_escalated()
    {
        AttentionReason reason = AttentionSamples.For(AttentionCode.ImplementationFailed);

        AttentionStageResult result = await _stage.TryAsync(new AttentionCase(_spec.Id, null, reason), AttentionFixture.Token);

        Assert.Equal(AttentionStageStatus.NotApplicable, result.Status);
        Assert.Empty(_f.Agents.Started);
    }

    // ----- Switch ------------------------------------------------------------------------------------------------

    [Fact]
    public async Task the_global_switch_turns_the_troubleshooter_off()
    {
        _f.Run.GlobalSettings.TroubleshooterEnabled = false;
        (TicketRun ticket, _) = Park(AttentionReasons.ImplementerBlocked("no database"), WorktreeStatus.Clean);

        AttentionTriageOutcome outcome = await Triage(ticket);

        Assert.Equal(AttentionTriageOutcome.NeedsUser, outcome);
        Assert.Empty(_f.Agents.Started);
        Assert.Null(ticket.Attention!.Diagnosis);
    }

    [Fact]
    public async Task a_repository_override_turns_it_off_or_on_against_the_global_switch()
    {
        SettingsProfile repository = SettingsProfile.ForRepository(_f.Run.Repository.Id);
        repository.TroubleshooterEnabled = false;
        _f.Run.Store.Add(repository);
        (TicketRun ticket, _) = Park(AttentionReasons.ImplementerBlocked("no database"), WorktreeStatus.Clean);

        await Triage(ticket);
        Assert.Empty(_f.Agents.Started);

        repository.TroubleshooterEnabled = true;
        _f.Run.GlobalSettings.TroubleshooterEnabled = false;
        _f.Agents.Script(AgentRole.Troubleshooter, _ => Report(TroubleshooterOutcome.CannotResolve));
        await Triage(ticket);
        Assert.Single(_f.Agents.Started);
    }

    // ----- The agent session --------------------------------------------------------------------------------------

    [Fact]
    public async Task the_session_is_a_normal_step_with_the_roles_model_policy_and_the_problem_in_its_prompt()
    {
        _f.Run.GlobalSettings.SetRole(AgentRole.Troubleshooter, new RoleSettingsOverride("gpt-6-luna", "low", AttentionFixture.ShippedTemplate(), 600));
        _f.Logs.Add(new AgentLogView(3, RunControlFixture.T0, AgentLogKind.Assistant, "Implementer said: the build is red"));
        (TicketRun ticket, string path) = Park(AttentionReasons.ImplementerBlocked("no database"), WorktreeStatus.Clean);
        _f.Run.SeedRunningStep(_spec, ticket, StepKind.Implement, AgentRole.Implementer).Finish(StepStatus.Failed, RunControlFixture.T0, failureReason: "blocked");
        _f.Agents.Script(AgentRole.Troubleshooter, _ => Report(TroubleshooterOutcome.CannotResolve));

        await Triage(ticket);

        AgentRunRequest request = Assert.Single(_f.Agents.Started);
        Assert.Equal(("gpt-6-luna", "low"), (request.Settings.Model, request.Settings.ReasoningEffort));
        Assert.Equal(TimeSpan.FromSeconds(600), request.Settings.Timeout);
        Assert.Contains("ImplementerBlocked", request.Prompt, StringComparison.Ordinal);
        Assert.Contains("no database", request.Prompt, StringComparison.Ordinal);
        Assert.Contains("the build is red", request.Prompt, StringComparison.Ordinal);
        Assert.Contains("Ticket 1", request.Prompt, StringComparison.Ordinal);
        Assert.Contains(path, request.Prompt, StringComparison.Ordinal);
        Assert.True(request.Policy.Paths.CanWrite(Path.Combine(path, "src", "a.cs")));
        Assert.True(request.Policy.Paths.CanWrite(Path.Combine(_f.LayoutOf(_spec).TroubleshooterIntegrationWorktree, "a.cs")));
        Assert.False(request.Policy.Paths.CanWrite(Path.Combine(_f.LayoutOf(_spec).RunDirectory, "tickets", "other", "a.cs")));
        Assert.False(request.Policy.IsCommandAllowed($"git push --force origin {ticket.BranchName}"));
        Assert.False(request.Policy.IsCommandAllowed($"git checkout {_spec.IntegrationBranch}"));
        Assert.True(request.Policy.IsCommandAllowed("git reset --hard HEAD"));
        StepRun step = Assert.Single(_f.Run.Store.Steps, candidate => candidate.Kind == StepKind.Troubleshoot);
        Assert.Equal((StepKind.Troubleshoot, AgentRole.Troubleshooter, StepStatus.Succeeded), (step.Kind, step.AgentRole, step.Status));
        Assert.Equal(("gpt-6-luna", "low"), (step.Model, step.ReasoningEffort));
        Assert.Equal(ticket.Id, step.TicketRunId);
    }

    [Fact]
    public async Task uncommitted_changes_and_the_branch_tips_are_backed_up_before_the_agent_starts()
    {
        (TicketRun ticket, string path) = Park(AttentionReasons.WorktreeNotClean("w", "b", "dirty"), WorktreeStatus.Locked);
        _f.Git.SetWorktreeChanges(path, new WorktreeChanges("diff --git a/x b/x", ["x"], ["new.txt"], []));
        _f.Git.SetWorktreeStatus(path, WorktreeStatus.Locked);
        string[] backups = [];
        _f.Agents.Script(AgentRole.Troubleshooter, request =>
        {
            string directory = Path.Combine(_f.LayoutOf(_spec).TroubleshooterBackupsDirectory, ticket.Id.Value);
            backups = [.. Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories).Select(Path.GetFileName).OfType<string>()];
            Assert.True(request.Policy.Paths.CanWrite(Path.Combine(directory, "extra.patch")));
            return Report(TroubleshooterOutcome.CannotResolve);
        });

        await Triage(ticket);

        Assert.Contains("tracked-changes.patch", backups);
        Assert.Contains("untracked-files.txt", backups);
        Assert.Contains("refs.txt", backups);
    }

    [Fact]
    public async Task a_verified_repair_resumes_the_phase_and_is_shown_as_a_step_and_in_the_events()
    {
        (TicketRun ticket, string path) = Park(AttentionReasons.WorktreeNotClean("w", "b", "dirty"), WorktreeStatus.Locked);
        _f.Agents.Script(AgentRole.Troubleshooter, _ =>
        {
            _f.Git.SetWorktreeStatus(path, WorktreeStatus.Clean);
            return Report(TroubleshooterOutcome.Resolved, summary: "Removed a stale lock.", actions: ["rm .git/index.lock"], verification: "git status is clean");
        });

        AttentionTriageOutcome outcome = await Triage(ticket);

        Assert.Equal(AttentionTriageOutcome.Resolved, outcome);
        Assert.Equal(TicketRunStatus.Reviewing, ticket.Status);
        Assert.Null(ticket.Attention);
        Assert.Equal(StepStatus.Succeeded, Assert.Single(_f.Run.Store.Steps).Status);
        RunEvent[] events = [.. _f.EventsOf(_spec)];
        Assert.Contains(events, runEvent => runEvent.Type == AttentionRunEvents.AutoResolved && runEvent.PayloadJson.Contains("Removed a stale lock", StringComparison.Ordinal) && runEvent.PayloadJson.Contains("Troubleshooter", StringComparison.Ordinal));
        Assert.Contains(events, runEvent => runEvent.Type == "ControlAutoRetry");
        using JsonDocument finished = JsonDocument.Parse(events.Single(runEvent => runEvent.Type == TroubleshooterRunEvents.Finished).PayloadJson);
        Assert.True(finished.RootElement.GetProperty("verified").GetBoolean());
    }

    [Fact]
    public async Task a_false_claim_is_rejected_by_webdevloops_own_check_and_the_user_gets_the_diagnosis()
    {
        (TicketRun ticket, _) = Park(AttentionReasons.WorktreeNotClean("w", "b", "dirty"), WorktreeStatus.Locked);
        _f.Agents.Script(AgentRole.Troubleshooter, _ => Report(
            TroubleshooterOutcome.Resolved, summary: "Everything is fine now.", actions: ["looked around"], verification: "I believe it is clean"));

        AttentionTriageOutcome outcome = await Triage(ticket);

        Assert.Equal(AttentionTriageOutcome.NeedsUser, outcome);
        Assert.Equal(TicketRunStatus.NeedsAttention, ticket.Status);
        AttentionReason reason = ticket.Attention!;
        Assert.Equal("Everything is fine now.", reason.Diagnosis!.Summary);
        Assert.Contains(reason.TriedSoFar, tried => tried.Contains("own check disagreed", StringComparison.Ordinal) && tried.Contains("Locked", StringComparison.Ordinal));
        StepRun step = Assert.Single(_f.Run.Store.Steps);
        Assert.Equal(StepStatus.Failed, step.Status);
        Assert.Contains("own check disagreed", step.FailureReason, StringComparison.Ordinal);
        Assert.DoesNotContain(AttentionRunEvents.AutoResolved, _f.EventsOf(_spec).Select(runEvent => runEvent.Type));
    }

    [Fact]
    public async Task a_claim_with_a_dirty_worktree_or_the_wrong_branch_is_rejected()
    {
        (TicketRun ticket, string path) = Park(AttentionReasons.ReportedCommitMismatch("mismatch"), WorktreeStatus.Clean);
        _f.Agents.Script(AgentRole.Troubleshooter, _ =>
        {
            _f.Git.PrepareWorktreeAsync(_f.Location, new WorktreeSpec(new BranchName("some/other-branch"), _f.Base, path), AttentionFixture.Token).GetAwaiter().GetResult();
            return Report(TroubleshooterOutcome.Resolved, verification: "checked");
        });

        await Triage(ticket);

        Assert.Equal(TicketRunStatus.NeedsAttention, ticket.Status);
        Assert.Contains(ticket.Attention!.TriedSoFar, tried => tried.Contains("some/other-branch", StringComparison.Ordinal));
    }

    [Fact]
    public async Task new_commits_of_the_agent_are_reviewed_before_the_work_continues()
    {
        (TicketRun ticket, string path) = Park(AttentionReasons.ImplementationFailed(3, "the build is red"), WorktreeStatus.Clean, TicketRunStatus.Implementing);
        CommitSha fixedHead = default;
        _f.Agents.Script(AgentRole.Troubleshooter, _ =>
        {
            fixedHead = _f.Git.CommitInWorktree(path, "calc.py");
            return Report(TroubleshooterOutcome.Resolved, actions: ["fixed the build"], verification: "dotnet build passes");
        });

        AttentionTriageOutcome outcome = await Triage(ticket);

        Assert.Equal(AttentionTriageOutcome.Resolved, outcome);
        Assert.Equal(TicketRunStatus.Reviewing, ticket.Status);
        Assert.Equal(fixedHead, ticket.LastImplementedSha);
    }

    [Fact]
    public async Task a_session_that_dropped_commits_is_undone_and_its_claim_rejected()
    {
        (TicketRun ticket, string path) = Park(AttentionReasons.ImplementerBlocked("blocked"), WorktreeStatus.Clean);
        CommitSha tip = (await _f.Git.GetBranchTipAsync(_f.Location, ticket.BranchName, GitRefScope.Local, AttentionFixture.Token))!.Value;
        _f.Agents.Script(AgentRole.Troubleshooter, _ =>
        {
            _f.Git.UpdateBranchAsync(_f.Location, ticket.BranchName, _f.Base, tip, AttentionFixture.Token).GetAwaiter().GetResult();
            return Report(TroubleshooterOutcome.Resolved, verification: "reset to a known good state");
        });

        await Triage(ticket);

        Assert.Equal(tip, await _f.Git.GetBranchTipAsync(_f.Location, ticket.BranchName, GitRefScope.Local, AttentionFixture.Token));
        Assert.Equal(TicketRunStatus.NeedsAttention, ticket.Status);
        Assert.Contains(ticket.Attention!.TriedSoFar, tried => tried.Contains("restored", StringComparison.Ordinal));
        Assert.True(path.Length > 0);
    }

    [Fact]
    public async Task the_diagnosis_populates_the_action_needed_card()
    {
        (TicketRun ticket, _) = Park(AttentionReasons.ImplementerBlocked("no database"), WorktreeStatus.Clean);
        _f.Agents.Script(AgentRole.Troubleshooter, _ => Report(
            TroubleshooterOutcome.NeedsUser,
            summary: "The ticket needs a database that is not installed on this machine.",
            actions: ["Ran dotnet test: 12 failures", "Checked that postgres is missing"],
            userSteps: ["Install PostgreSQL 16.", "Retry the ticket."],
            buttons: [AttentionActionKind.Skip, AttentionActionKind.Retry]));

        await Triage(ticket);

        AttentionReason reason = ticket.Attention!;
        Assert.Equal("The ticket needs a database that is not installed on this machine.", reason.Diagnosis!.Summary);
        Assert.Equal("Install PostgreSQL 16.", reason.UserSteps[0].Text);
        Assert.Contains(reason.TriedSoFar, tried => tried == "Troubleshooter: Checked that postgres is missing");
        Assert.Equal(AttentionActionKind.Skip, reason.PrimaryAction.Kind);
        Assert.Contains(AttentionRunEvents.NeedsYou, _f.EventsOf(_spec).Select(runEvent => runEvent.Type));
    }

    [Fact]
    public async Task an_agent_that_never_reports_ends_the_step_as_failed_and_the_user_is_asked()
    {
        (TicketRun ticket, _) = Park(AttentionReasons.ImplementerBlocked("no database"), WorktreeStatus.Clean);

        AttentionTriageOutcome outcome = await Triage(ticket);

        Assert.Equal(AttentionTriageOutcome.NeedsUser, outcome);
        StepRun step = Assert.Single(_f.Run.Store.Steps);
        Assert.Equal(StepStatus.Failed, step.Status);
        Assert.Contains(ticket.Attention!.TriedSoFar, tried => tried.StartsWith("Troubleshooter:", StringComparison.Ordinal));
    }

    // ----- Bounds: no loops -------------------------------------------------------------------------------------

    [Fact]
    public async Task attempts_are_limited_per_item_even_when_the_state_keeps_changing()
    {
        (TicketRun ticket, string path) = Park(AttentionReasons.ImplementerBlocked("no database"), WorktreeStatus.Clean, TicketRunStatus.Implementing);
        for (int attempt = 0; attempt < 3; attempt++)
        {
            _f.Agents.Script(AgentRole.Troubleshooter, _ =>
            {
                _f.Git.CommitInWorktree(path, $"change{attempt}.py");
                return Report(TroubleshooterOutcome.CannotResolve);
            });
        }

        for (int round = 0; round < 3; round++)
        {
            Assert.Equal(AttentionTriageOutcome.NeedsUser, await Triage(ticket));
            if (round < 2)
            {
                ticket.UpdateAttention(AttentionReasons.ImplementerBlocked("no database"), RunControlFixture.T0);
            }
        }

        Assert.Equal(2, _f.Agents.Started.Count);
        Assert.Equal(2, _f.Run.Store.Steps.Count(step => step.Kind == StepKind.Troubleshoot));
        Assert.Contains(ticket.Attention?.TriedSoFar ?? [], tried => tried.Contains("2 time(s)", StringComparison.Ordinal));
    }

    [Fact]
    public async Task the_attempt_limit_comes_from_the_settings()
    {
        _f.Run.GlobalSettings.TroubleshooterMaxAttempts = 1;
        (TicketRun ticket, string path) = Park(AttentionReasons.ImplementerBlocked("no database"), WorktreeStatus.Clean, TicketRunStatus.Implementing);
        _f.Agents.Script(AgentRole.Troubleshooter, _ =>
        {
            _f.Git.CommitInWorktree(path, "change.py");
            return Report(TroubleshooterOutcome.CannotResolve);
        });

        await Triage(ticket);
        ticket.UpdateAttention(AttentionReasons.ImplementerBlocked("no database"), RunControlFixture.T0);
        await Triage(ticket);

        Assert.Single(_f.Agents.Started);
    }

    [Fact]
    public async Task an_unchanged_state_is_never_escalated_twice_and_keeps_the_earlier_diagnosis()
    {
        (TicketRun ticket, _) = Park(AttentionReasons.ImplementerBlocked("no database"), WorktreeStatus.Clean);
        _f.Agents.Script(AgentRole.Troubleshooter, _ => Report(TroubleshooterOutcome.NeedsUser, summary: "Needs postgres.", userSteps: ["Install it."]));
        await Triage(ticket);

        // The user retries, the same thing happens again in the same git state.
        ticket.UpdateAttention(AttentionReasons.ImplementerBlocked("no database"), RunControlFixture.T0);
        _f.Run.Store.Add(RunEvent.Create(_spec.Id, ticket.Id, "ControlRetry", "{}", RunControlFixture.T0.AddMinutes(5)));
        AttentionTriageOutcome outcome = await Triage(ticket);

        Assert.Equal(AttentionTriageOutcome.NeedsUser, outcome);
        Assert.Single(_f.Agents.Started);
        Assert.Equal("Needs postgres.", ticket.Attention!.Diagnosis!.Summary);
        Assert.Contains(ticket.Attention.TriedSoFar, tried => tried.Contains("unchanged state", StringComparison.Ordinal));
    }

    [Fact]
    public async Task a_changed_state_after_the_users_retry_may_be_escalated_again()
    {
        (TicketRun ticket, string path) = Park(AttentionReasons.ImplementerBlocked("no database"), WorktreeStatus.Clean);
        _f.Agents.Script(AgentRole.Troubleshooter, _ => Report(TroubleshooterOutcome.CannotResolve));
        _f.Agents.Script(AgentRole.Troubleshooter, _ => Report(TroubleshooterOutcome.CannotResolve));
        await Triage(ticket);

        _f.Git.CommitInWorktree(path, "manual-fix.py");
        ticket.UpdateAttention(AttentionReasons.ImplementerBlocked("no database"), RunControlFixture.T0);
        _f.Run.Store.Add(RunEvent.Create(_spec.Id, ticket.Id, "ControlRetry", "{}", RunControlFixture.T0.AddMinutes(5)));
        await Triage(ticket);

        Assert.Equal(2, _f.Agents.Started.Count);
    }

    // ----- Aborting cancels the session ------------------------------------------------------------------------

    [Fact]
    public async Task a_session_cancelled_while_it_ran_is_left_cancelled_and_nothing_resumes()
    {
        (TicketRun ticket, string path) = Park(AttentionReasons.WorktreeNotClean("w", "b", "dirty"), WorktreeStatus.Locked);
        _f.Agents.Script(AgentRole.Troubleshooter, _ =>
        {
            _f.Git.SetWorktreeStatus(path, WorktreeStatus.Clean);
            _f.Run.Store.Steps.Single().Finish(StepStatus.Cancelled, RunControlFixture.T0, failureReason: "Cancelled by the user.");
            return Report(TroubleshooterOutcome.Resolved, verification: "clean");
        });

        AttentionTriageOutcome outcome = await Triage(ticket);

        Assert.Equal(AttentionTriageOutcome.NeedsUser, outcome);
        Assert.Equal(TicketRunStatus.NeedsAttention, ticket.Status);
        Assert.Equal(StepStatus.Cancelled, Assert.Single(_f.Run.Store.Steps).Status);
    }

    [Fact]
    public async Task aborting_the_ticket_cancels_the_running_troubleshooter_and_aborts_its_session()
    {
        (TicketRun ticket, _) = Park(AttentionReasons.ImplementerBlocked("no database"), WorktreeStatus.Clean);
        StepRun running = _f.Run.SeedRunningStep(_spec, ticket, StepKind.Troubleshoot, AgentRole.Troubleshooter);
        (_, TicketRunControl control) = _f.Run.Controls();

        Assert.True((await control.AbortAsync(ticket.Id, AttentionFixture.Token)).IsApplied);

        Assert.Equal(StepStatus.Cancelled, running.Status);
        Assert.Contains(new AgentSessionId(running.CopilotSessionId!), _f.Agents.Aborted);
    }

    [Fact]
    public async Task the_users_retry_or_skip_stops_a_troubleshooter_that_is_still_working_but_the_automatic_retry_does_not()
    {
        (TicketRun ticket, _) = Park(AttentionReasons.ImplementerBlocked("no database"), WorktreeStatus.Clean);
        StepRun running = _f.Run.SeedRunningStep(_spec, ticket, StepKind.Troubleshoot, AgentRole.Troubleshooter);
        (_, TicketRunControl control) = _f.Run.Controls();

        Assert.True((await control.RetryAsync(ticket.Id, AttentionFixture.Token, automatic: true)).IsApplied);
        Assert.Equal(StepStatus.Running, running.Status);

        ticket.MarkNeedsAttention(AttentionReasons.ImplementerBlocked("again"), RunControlFixture.T0);
        Assert.True((await control.RetryAsync(ticket.Id, AttentionFixture.Token)).IsApplied);
        Assert.Equal(StepStatus.Cancelled, running.Status);
        Assert.Contains(new AgentSessionId(running.CopilotSessionId!), _f.Agents.Aborted);
    }

    // ----- Helpers -------------------------------------------------------------------------------------------------

    private (TicketRun Ticket, string Path) Park(AttentionReason reason, WorktreeStatus worktree, params TicketRunStatus[] path)
    {
        CommitSha reviewed = _f.Git.Commit([_f.Base], "calc.py");
        TicketRun ticket = _f.SeedParkedTicket(_spec, reason, reviewed, path.Length == 0 ? AttentionFixture.ToReviewing : [.. AttentionFixture.ToReviewing.Take(1), .. path]);
        string worktreePath = _f.TicketPath(_spec, ticket);
        _f.Git.PrepareWorktreeAsync(_f.Location, new WorktreeSpec(ticket.BranchName, reviewed, worktreePath), AttentionFixture.Token).GetAwaiter().GetResult();
        if (worktree != WorktreeStatus.Clean)
        {
            _f.Git.SetWorktreeStatus(worktreePath, worktree);
        }

        return (ticket, worktreePath);
    }

    private Task<AttentionTriageOutcome> Triage(TicketRun ticket) =>
        _f.Triage(_stage).TriageAsync(new AttentionTriageAssignment(ticket.SpecRunId, ticket.Id), AttentionFixture.Token);

    private AttentionCase Case(TicketRun ticket) => new(_spec.Id, ticket.Id, ticket.Attention!);

    private static TroubleshooterReport Report(
        TroubleshooterOutcome outcome,
        string summary = "I looked at it.",
        string[]? actions = null,
        string verification = "",
        string[]? userSteps = null,
        AttentionActionKind[]? buttons = null) =>
        new(outcome, summary, actions ?? [], verification, userSteps ?? [], buttons ?? []);
}
