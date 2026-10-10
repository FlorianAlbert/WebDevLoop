using System.Text.Json;
using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Orchestration.Attention;
using WebDevLoop.Core.Orchestration.Control;
using WebDevLoop.Core.Ports;
using WebDevLoop.Core.Tests.Domain.Attention;
using WebDevLoop.Core.Tests.Orchestration.Control;

namespace WebDevLoop.Core.Tests.Orchestration.Attention;

public sealed class AttentionTriageTests : IDisposable
{
    private readonly AttentionFixture _f = new();
    private readonly SpecRun _spec;

    public AttentionTriageTests() => _spec = _f.SeedRunningSpec();

    public void Dispose() => _f.Dispose();

    // ----- Auto: dirty worktree ----------------------------------------------------------------------------------

    [Fact]
    public async Task a_dirty_ticket_worktree_is_cleaned_verified_and_the_review_resumes_without_the_user()
    {
        CommitSha reviewed = _f.Git.Commit([_f.Base], "calc.py");
        TicketRun ticket = ParkedAt(AttentionReasons.WorktreeNotClean("w", "b", "dirty"), reviewed, AttentionFixture.ToReviewing);
        string path = Prepare(ticket, reviewed);
        _f.Git.SetWorktreeChanges(path, new WorktreeChanges(string.Empty, [], ["__pycache__/calc.pyc"], []));

        AttentionTriageOutcome outcome = await _f.Triage().TriageAsync(Assignment(ticket), AttentionFixture.Token);

        Assert.Equal(AttentionTriageOutcome.Resolved, outcome);
        Assert.Equal(TicketRunStatus.Reviewing, ticket.Status);
        Assert.Null(ticket.Attention);
        Assert.Equal(WorktreeStatus.Clean, (await _f.Git.InspectWorktreeAsync(_f.Location, path, AttentionFixture.Token)).Status);
        string[] types = [.. _f.EventsOf(_spec).Select(runEvent => runEvent.Type)];
        Assert.Contains(AttentionRunEvents.AutoResolved, types);
        Assert.Contains("ControlAutoRetry", types);
        Assert.Contains("WorktreeRemediated", types);
        Assert.DoesNotContain("ControlRetry", types);
    }

    [Fact]
    public async Task a_worktree_that_stays_dirty_goes_to_the_user_with_what_was_tried()
    {
        CommitSha reviewed = _f.Git.Commit([_f.Base], "calc.py");
        TicketRun ticket = ParkedAt(AttentionReasons.WorktreeNotClean("w", "b", "dirty"), reviewed, AttentionFixture.ToReviewing);
        await _f.Git.PrepareWorktreeAsync(_f.Location, new WorktreeSpec(ticket.BranchName, reviewed, AttentionFixture.WorktreeOf(_spec, ticket)), AttentionFixture.Token);
        _f.Git.SetWorktreeStatus(AttentionFixture.WorktreeOf(_spec, ticket), WorktreeStatus.Locked);

        AttentionTriageOutcome outcome = await _f.Triage().TriageAsync(Assignment(ticket), AttentionFixture.Token);

        Assert.Equal(AttentionTriageOutcome.NeedsUser, outcome);
        Assert.Equal(TicketRunStatus.NeedsAttention, ticket.Status);
        AttentionReason reason = ticket.Attention!;
        Assert.True(reason.AutoFix!.Attempted);
        Assert.False(reason.AutoFixPending);
        Assert.Contains(reason.TriedSoFar, tried => tried.Contains("Locked", StringComparison.Ordinal));
        Assert.Contains(AttentionRunEvents.NeedsYou, _f.EventsOf(_spec).Select(runEvent => runEvent.Type));
    }

    // ----- Auto: ticket branch not based on the integration branch --------------------------------------------

    [Fact]
    public async Task a_ticket_branch_behind_the_integration_branch_gets_the_tip_merged_and_is_reviewed_again()
    {
        CommitSha reviewed = _f.Git.Commit([_f.Base], "calc.py");
        CommitSha tip = _f.Git.Commit([_f.Base], "other.py");
        await _f.Git.UpdateBranchAsync(_f.Location, _spec.IntegrationBranch, tip, _f.Base, AttentionFixture.Token);
        _spec.IntegrationTipSha = tip;
        TicketRun ticket = ParkedAt(AttentionReasons.TicketBranchNotBasedOnIntegration("b", "not based"), reviewed, AttentionFixture.ToReviewing);
        await _f.Git.UpdateBranchAsync(_f.Location, ticket.BranchName, reviewed, null, AttentionFixture.Token);

        AttentionTriageOutcome outcome = await _f.Triage().TriageAsync(Assignment(ticket), AttentionFixture.Token);

        Assert.Equal(AttentionTriageOutcome.Resolved, outcome);
        Assert.Equal(TicketRunStatus.Reviewing, ticket.Status);
        CommitSha merged = ticket.LastImplementedSha!.Value;
        Assert.NotEqual(reviewed, merged);
        Assert.True(await _f.Git.IsAncestorAsync(_f.Location, tip, merged, AttentionFixture.Token));
        Assert.True(await _f.Git.IsAncestorAsync(_f.Location, reviewed, merged, AttentionFixture.Token));
    }

    [Fact]
    public async Task a_missing_integration_merge_after_implementation_resumes_in_review_not_in_a_new_implementation()
    {
        CommitSha implemented = _f.Git.Commit([_f.Base], "calc.py");
        CommitSha tip = _f.Git.Commit([_f.Base], "other.py");
        await _f.Git.UpdateBranchAsync(_f.Location, _spec.IntegrationBranch, tip, _f.Base, AttentionFixture.Token);
        TicketRun ticket = ParkedAt(
            AttentionReasons.TicketBranchNotBasedOnIntegration("b", "implementer did not merge"), null, [TicketRunStatus.Ready, TicketRunStatus.Implementing]);
        await _f.Git.UpdateBranchAsync(_f.Location, ticket.BranchName, implemented, null, AttentionFixture.Token);

        await _f.Triage().TriageAsync(Assignment(ticket), AttentionFixture.Token);

        Assert.Equal(TicketRunStatus.Reviewing, ticket.Status);
        Assert.NotNull(ticket.LastImplementedSha);
    }

    // ----- Auto: transient GitHub / network failures ---------------------------------------------------------

    [Fact]
    public async Task a_temporary_integration_failure_is_retried_after_a_pause_and_resumes_the_saga()
    {
        TicketRun ticket = ParkedAt(AttentionReasons.IntegrationTemporaryFailure(3, "StackBranchPushed", "timeout"), _f.Base, AttentionFixture.ToIntegrating);

        AttentionTriageOutcome outcome = await _f.Triage().TriageAsync(Assignment(ticket), AttentionFixture.Token);

        Assert.Equal(AttentionTriageOutcome.Resolved, outcome);
        Assert.Equal(TicketRunStatus.Integrating, ticket.Status);
        Assert.Equal([TimeSpan.FromSeconds(10)], _f.Pauses);
    }

    [Fact]
    public async Task temporary_failures_back_off_and_are_bounded_then_the_user_is_asked()
    {
        TicketRun ticket = ParkedAt(AttentionReasons.IntegrationTemporaryFailure(3, "StackBranchPushed", "timeout"), _f.Base, AttentionFixture.ToIntegrating);
        AttentionTriageService triage = _f.Triage();
        for (int attempt = 0; attempt < 3; attempt++)
        {
            Assert.Equal(AttentionTriageOutcome.Resolved, await triage.TriageAsync(Assignment(ticket), AttentionFixture.Token));
            ticket.MarkNeedsAttention(AttentionReasons.IntegrationTemporaryFailure(4 + attempt, "StackBranchPushed", "timeout"), RunControlFixture.T0);
        }

        AttentionTriageOutcome outcome = await triage.TriageAsync(Assignment(ticket), AttentionFixture.Token);

        Assert.Equal([TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(20), TimeSpan.FromSeconds(30)], _f.Pauses);
        Assert.Equal(AttentionTriageOutcome.NeedsUser, outcome);
        Assert.Equal(TicketRunStatus.NeedsAttention, ticket.Status);
        Assert.Contains(ticket.Attention!.TriedSoFar, tried => tried.Contains("3 time(s)", StringComparison.Ordinal));
    }

    [Fact]
    public async Task the_users_own_retry_gives_the_remediation_a_fresh_budget()
    {
        TicketRun ticket = ParkedAt(AttentionReasons.IntegrationTemporaryFailure(3, "StackBranchPushed", "timeout"), _f.Base, AttentionFixture.ToIntegrating);
        AttentionTriageService triage = _f.Triage();
        for (int attempt = 0; attempt < 3; attempt++)
        {
            await triage.TriageAsync(Assignment(ticket), AttentionFixture.Token);
            ticket.MarkNeedsAttention(AttentionReasons.IntegrationTemporaryFailure(4, "StackBranchPushed", "timeout"), RunControlFixture.T0);
        }

        ControlResult retried = await _f.Run.Control().RetryTicketAsync(ticket.Id, AttentionFixture.Token);
        ticket.MarkNeedsAttention(AttentionReasons.IntegrationTemporaryFailure(4, "StackBranchPushed", "timeout"), RunControlFixture.T0);
        AttentionTriageOutcome outcome = await triage.TriageAsync(Assignment(ticket), AttentionFixture.Token);

        Assert.True(retried.IsApplied);
        Assert.Equal(AttentionTriageOutcome.Resolved, outcome);
        Assert.Equal(4, _f.Pauses.Count);
    }

    // ----- Auto: stale own integration branch ----------------------------------------------------------------

    [Fact]
    public async Task a_leftover_integration_branch_of_the_same_run_is_reset_to_the_runs_base_and_preparation_restarts()
    {
        CommitSha leftover = _f.Git.Commit([_f.Base], "stale.py");
        await _f.Git.UpdateBranchAsync(_f.Location, _spec.IntegrationBranch, leftover, _f.Base, AttentionFixture.Token);
        SpecRun spec = ParkedSpec(AttentionReasons.IntegrationBranchExists("b", "exists"), SpecRunStatus.Preparing);
        spec.IntegrationBaseSha = _f.Base;
        spec.IntegrationTipSha = _f.Base;
        await _f.Git.UpdateBranchAsync(_f.Location, spec.IntegrationBranch, leftover, _f.Git.RemoteTip(spec.IntegrationBranch), AttentionFixture.Token);

        AttentionTriageOutcome outcome = await _f.Triage().TriageAsync(new AttentionTriageAssignment(spec.Id, null), AttentionFixture.Token);

        Assert.Equal(AttentionTriageOutcome.Resolved, outcome);
        Assert.Equal(SpecRunStatus.Preparing, spec.Status);
        Assert.Equal(_f.Base, await _f.Git.GetBranchTipAsync(_f.Location, spec.IntegrationBranch, GitRefScope.Local, AttentionFixture.Token));
    }

    [Fact]
    public async Task an_integration_branch_that_already_carries_integrated_work_is_never_reset()
    {
        CommitSha leftover = _f.Git.Commit([_f.Base], "layer.py");
        await _f.Git.UpdateBranchAsync(_f.Location, _spec.IntegrationBranch, leftover, _f.Base, AttentionFixture.Token);
        _spec.IntegrationTipSha = leftover;
        _spec.TransitionTo(SpecRunStatus.ParentReviewing, RunControlFixture.T0);
        _spec.MarkNeedsAttention(AttentionReasons.IntegrationBranchExists("b", "exists"), RunControlFixture.T0);

        AttentionTriageOutcome outcome = await _f.Triage().TriageAsync(new AttentionTriageAssignment(_spec.Id, null), AttentionFixture.Token);

        Assert.Equal(AttentionTriageOutcome.NeedsUser, outcome);
        Assert.Equal(SpecRunStatus.NeedsAttention, _spec.Status);
        Assert.Equal(leftover, await _f.Git.GetBranchTipAsync(_f.Location, _spec.IntegrationBranch, GitRefScope.Local, AttentionFixture.Token));
    }

    // ----- Auto: exploration -----------------------------------------------------------------------------------

    [Fact]
    public async Task a_failed_exploration_is_retried_once_and_the_second_failure_goes_to_the_user()
    {
        SpecRun spec = ParkedSpec(AttentionReasons.ExplorationFailed("timed out"), SpecRunStatus.Preparing);
        AttentionTriageService triage = _f.Triage();

        AttentionTriageOutcome first = await triage.TriageAsync(new AttentionTriageAssignment(spec.Id, null), AttentionFixture.Token);
        spec.MarkNeedsAttention(AttentionReasons.ExplorationFailed("timed out again"), RunControlFixture.T0);
        AttentionTriageOutcome second = await triage.TriageAsync(new AttentionTriageAssignment(spec.Id, null), AttentionFixture.Token);

        Assert.Equal(AttentionTriageOutcome.Resolved, first);
        Assert.Equal(AttentionTriageOutcome.NeedsUser, second);
        Assert.Equal(SpecRunStatus.NeedsAttention, spec.Status);
        Assert.False(spec.Attention!.AutoFixPending);
    }

    // ----- Auto: ticket without changes ---------------------------------------------------------------------------

    [Fact]
    public async Task a_reviewed_ticket_that_adds_nothing_is_skipped_as_no_changes_needed_and_its_dependents_are_released()
    {
        TicketRun ticket = ParkedAt(AttentionReasons.TicketHasNoChanges("b", "abc", "def"), _f.Base, AttentionFixture.ToIntegrating);
        TicketRun dependent = _f.Run.SeedTicket(_spec);
        _f.Run.Block(dependent, ticket);

        AttentionTriageOutcome outcome = await _f.Triage().TriageAsync(Assignment(ticket), AttentionFixture.Token);

        Assert.Equal(AttentionTriageOutcome.Resolved, outcome);
        Assert.Equal(TicketRunStatus.Skipped, ticket.Status);
        RunEvent resolved = Assert.Single(_f.EventsOf(_spec), runEvent => runEvent.Type == AttentionRunEvents.AutoResolved);
        Assert.Contains("needs no changes", resolved.PayloadJson, StringComparison.Ordinal);
        Assert.Contains("ControlAutoSkip", _f.EventsOf(_spec).Select(runEvent => runEvent.Type));
    }

    [Fact]
    public async Task a_ticket_that_was_published_before_is_not_skipped_automatically()
    {
        TicketRun ticket = ParkedAt(AttentionReasons.TicketHasNoChanges("b", "abc", "def"), _f.Base, AttentionFixture.ToIntegrating);
        ticket.IntegratedCommitSha = _f.Base;

        AttentionTriageOutcome outcome = await _f.Triage().TriageAsync(Assignment(ticket), AttentionFixture.Token);

        Assert.Equal(AttentionTriageOutcome.NeedsUser, outcome);
        Assert.Equal(TicketRunStatus.NeedsAttention, ticket.Status);
    }

    // ----- Pipeline order ---------------------------------------------------------------------------------------

    [Fact]
    public async Task a_reason_for_the_user_is_only_recorded_and_nothing_is_changed()
    {
        TicketRun ticket = ParkedAt(AttentionReasons.BaseBranchMissing("trunk", "octo/app", "missing"), _f.Base, AttentionFixture.ToReviewing);
        AttentionReason before = ticket.Attention!;

        AttentionTriageOutcome outcome = await _f.Triage().TriageAsync(Assignment(ticket), AttentionFixture.Token);

        Assert.Equal(AttentionTriageOutcome.NeedsUser, outcome);
        Assert.Same(before, ticket.Attention);
        Assert.Equal(TicketRunStatus.NeedsAttention, ticket.Status);
        Assert.Equal([AttentionRunEvents.Raised], _f.EventsOf(_spec).Select(runEvent => runEvent.Type));
    }

    [Fact]
    public async Task a_run_that_no_longer_needs_attention_is_left_alone()
    {
        TicketRun ticket = ParkedAt(AttentionReasons.TicketHasNoChanges("b", "abc", "def"), _f.Base, AttentionFixture.ToIntegrating);
        await _f.Run.Control().AbortTicketAsync(ticket.Id, AttentionFixture.Token);

        AttentionTriageOutcome outcome = await _f.Triage().TriageAsync(Assignment(ticket), AttentionFixture.Token);

        Assert.Equal(AttentionTriageOutcome.NothingToDo, outcome);
        Assert.Equal(TicketRunStatus.Aborted, ticket.Status);
    }

    [Fact]
    public async Task a_troubleshooter_stage_registered_after_the_known_remediation_looks_at_what_remediation_could_not_resolve()
    {
        TicketRun ticket = ParkedAt(AttentionReasons.ImplementationFailed(3, "timed out"), null, [TicketRunStatus.Ready, TicketRunStatus.Implementing]);
        var troubleshooter = new RecordingStage("Troubleshooter", AttentionStageResult.Unresolved("The agent could not fix it.", "Troubleshooter session: cannot_resolve"));

        AttentionTriageOutcome outcome = await _f.Triage(troubleshooter).TriageAsync(Assignment(ticket), AttentionFixture.Token);

        Assert.Equal(AttentionTriageOutcome.NeedsUser, outcome);
        AttentionCase seen = Assert.Single(troubleshooter.Seen);
        Assert.Equal(AttentionCode.ImplementationFailed, seen.Reason.Code);
        Assert.Contains("Troubleshooter session: cannot_resolve", ticket.Attention!.TriedSoFar);
    }

    [Fact]
    public async Task a_troubleshooter_that_resolves_the_situation_resumes_the_work_and_is_never_asked_after_known_remediation_succeeded()
    {
        TicketRun failed = ParkedAt(AttentionReasons.ImplementationFailed(3, "timed out"), null, [TicketRunStatus.Ready, TicketRunStatus.Implementing]);
        var troubleshooter = new RecordingStage("Troubleshooter", AttentionStageResult.Resolved("The troubleshooter cleaned the tool cache.", AttentionResume.Retry));

        AttentionTriageOutcome outcome = await _f.Triage(troubleshooter).TriageAsync(Assignment(failed), AttentionFixture.Token);

        Assert.Equal(AttentionTriageOutcome.Resolved, outcome);
        Assert.Equal(TicketRunStatus.Ready, failed.Status);
        RunEvent resolved = Assert.Single(_f.EventsOf(_spec), runEvent => runEvent.Type == AttentionRunEvents.AutoResolved);
        using JsonDocument payload = JsonDocument.Parse(resolved.PayloadJson);
        Assert.Equal("Troubleshooter", payload.RootElement.GetProperty("stage").GetString());

        CommitSha reviewed = _f.Git.Commit([_f.Base], "calc.py");
        TicketRun dirty = ParkedAt(AttentionReasons.WorktreeNotClean("w", "b", "dirty"), reviewed, AttentionFixture.ToReviewing);
        Prepare(dirty, reviewed);
        var later = new RecordingStage("Troubleshooter", AttentionStageResult.Resolved("never", AttentionResume.Retry));

        await _f.Triage(later).TriageAsync(Assignment(dirty), AttentionFixture.Token);

        Assert.Empty(later.Seen);
    }

    // ----- Helpers ----------------------------------------------------------------------------------------------

    private TicketRun ParkedAt(AttentionReason reason, CommitSha? implemented, params TicketRunStatus[] path) =>
        _f.SeedParkedTicket(_spec, reason, implemented, path);

    private SpecRun ParkedSpec(AttentionReason reason, params SpecRunStatus[] path)
    {
        SpecRun spec = _f.Run.SeedSpec(1, path);
        spec.MarkNeedsAttention(reason, RunControlFixture.T0);
        return spec;
    }

    private string Prepare(TicketRun ticket, CommitSha head)
    {
        string path = AttentionFixture.WorktreeOf(_spec, ticket);
        _f.Git.PrepareWorktreeAsync(_f.Location, new WorktreeSpec(ticket.BranchName, head, path), AttentionFixture.Token).GetAwaiter().GetResult();
        return path;
    }

    private static AttentionTriageAssignment Assignment(TicketRun ticket) => new(ticket.SpecRunId, ticket.Id);

    private sealed class RecordingStage(string name, AttentionStageResult result) : IAttentionStage
    {
        public List<AttentionCase> Seen { get; } = [];

        public string Name => name;

        public Task<AttentionStageResult> TryAsync(AttentionCase attentionCase, CancellationToken cancellationToken)
        {
            Seen.Add(attentionCase);
            return Task.FromResult(result);
        }
    }
}
