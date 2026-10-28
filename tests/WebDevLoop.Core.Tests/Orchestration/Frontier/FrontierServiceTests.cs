using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Events;
using WebDevLoop.Core.Orchestration.Frontier;
using WebDevLoop.Core.Orchestration.TicketExecution;
using WebDevLoop.Core.Ports;
using WebDevLoop.Core.Tests.Orchestration.TicketExecution;

namespace WebDevLoop.Core.Tests.Orchestration.Frontier;

public sealed class FrontierServiceTests
{
    private readonly TicketExecutionFixture _fixture = new();

    [Fact]
    public async Task one_frontier_pass_uses_one_github_snapshot_for_dispatch_and_completion()
    {
        SeededSpec spec = await _fixture.SeedRunningSpecAsync("app", (1, []), (2, []));
        var missing = new IssueRef("elsewhere", "library", 900);
        _fixture.Issues.SetBlockedBy(_fixture.Ticket(spec[2]).Issue, missing);
        IssueRef parent = _fixture.Spec(spec.Id).ParentIssue;
        IssueRef first = _fixture.Ticket(spec[1]).Issue;
        IssueRef second = _fixture.Ticket(spec[2]).Issue;

        FrontierResult result = await _fixture.ReconcileAsync(spec.Id);

        Assert.Equal([spec[1]], result.Dispatched);
        Assert.Equal(FrontierOutcome.GitHubUnavailable, result.Outcome);
        Assert.Single(result.LookupErrors);
        Assert.Equal(SpecRunStatus.Running, _fixture.Spec(spec.Id).Status);
        Assert.Contains("unavailable", _fixture.Spec(spec.Id).WorkDecisionReason);
        Assert.Equal([parent], _fixture.Issues.SubIssueReads);
        Assert.Equal(4, _fixture.Issues.IssueReads.Count);
        foreach (IssueRef issue in new[] { parent, first, second, missing })
        {
            Assert.Single(_fixture.Issues.IssueReads, read => read == issue);
        }
    }

    [Fact]
    public async Task permission_changes_during_dispatch_apply_to_the_next_frontier_pass()
    {
        SeededSpec spec = await _fixture.SeedRunningSpecAsync("app", (1, []));
        IssueSnapshot parent = await _fixture.Issues.GetIssueAsync(_fixture.Spec(spec.Id).ParentIssue, TicketExecutionFixture.Token);
        _fixture.Launcher.Run = _ =>
        {
            _fixture.Issues.Seed(parent with { Labels = [] });
            return Task.CompletedTask;
        };
        var frontier = _fixture.Frontier();

        await frontier.ReconcileAsync(spec.Id, TicketExecutionFixture.Token);

        Assert.Equal(SpecRunStatus.Running, _fixture.Spec(spec.Id).Status);
        Assert.Null(_fixture.Spec(spec.Id).WorkDecisionReason);
        Assert.Equal(TicketRunStatus.Implementing, _fixture.Ticket(spec[1]).Status);

        await frontier.ReconcileAsync(spec.Id, TicketExecutionFixture.Token);

        Assert.Equal(SpecRunStatus.Running, _fixture.Spec(spec.Id).Status);
        Assert.Contains("opted out", _fixture.Spec(spec.Id).WorkDecisionReason);
        Assert.Single(_fixture.Launcher.Launched);
    }

    [Fact]
    public async Task ready_work_loses_permission_but_a_selected_ticket_can_retry_and_finish_after_parent_opt_out()
    {
        SeededSpec spec = await _fixture.SeedRunningSpecAsync("app", (1, []), (2, []));
        _fixture.Settings.Configure(spec.RepositoryId, settings => settings with { MaxConcurrentImplementersPerRepo = 1 });
        await _fixture.ReconcileAsync(spec.Id);
        Assert.Equal(TicketRunStatus.Ready, _fixture.Ticket(spec[2]).Status);
        IssueSnapshot second = await _fixture.Issues.GetIssueAsync(_fixture.Ticket(spec[2]).Issue, TicketExecutionFixture.Token);
        _fixture.Issues.Seed(second with { Labels = ["ready-for-agent"] });

        await _fixture.ReconcileAsync(spec.Id);
        Assert.Equal(TicketRunStatus.Blocked, _fixture.Ticket(spec[2]).Status);

        IssueSnapshot parent = await _fixture.Issues.GetIssueAsync(_fixture.Spec(spec.Id).ParentIssue, TicketExecutionFixture.Token);
        IssueSnapshot selected = await _fixture.Issues.GetIssueAsync(_fixture.Ticket(spec[1]).Issue, TicketExecutionFixture.Token);
        _fixture.Issues.Seed(parent with { Labels = [] });
        _fixture.Issues.Seed(selected with { Labels = [] });
        await _fixture.MoveAsync(spec[1], TicketRunStatus.NeedsAttention, TicketRunStatus.Ready);
        await _fixture.ReconcileAsync(spec.Id);

        Assert.Equal(2, _fixture.Ticket(spec[1]).Attempt);
        Assert.Equal(SpecRunStatus.Running, _fixture.Spec(spec.Id).Status);
        await _fixture.IntegrateAsync(spec, spec[1]);
        await _fixture.ReconcileAsync(spec.Id);

        Assert.Equal(SpecRunStatus.Completed, _fixture.Spec(spec.Id).Status);
        Assert.Contains("opted out", _fixture.Spec(spec.Id).WorkDecisionReason);
        Assert.Equal(TicketRunStatus.Blocked, _fixture.Ticket(spec[2]).Status);
        Assert.DoesNotContain(_fixture.Launcher.Launched, assignment => assignment.TicketRunId == spec[2]);
    }

    [Fact]
    public async Task a_repository_label_lookup_failure_defers_only_its_specs_and_recovery_resumes_them()
    {
        SeededSpec affected = await _fixture.SeedRunningSpecAsync("app", (1, []));
        SeededSpec other = await _fixture.SeedRunningSpecAsync("other", (1, []));
        var repository = new GitHubRepoRef("octo", "app");
        _fixture.Labels.LookupFailures[repository] = new RepositoryLabelException("Check Issues permissions and retry.");

        IReadOnlyList<FrontierResult> results = await _fixture.Frontier().ReconcileAllRunningAsync(TicketExecutionFixture.Token);

        Assert.Equal(FrontierOutcome.GitHubUnavailable, results[0].Outcome);
        Assert.Contains("Check Issues permissions and retry.", Assert.Single(results[0].LookupErrors).Message);
        Assert.Empty(results[0].Dispatched);
        Assert.Equal(TicketRunStatus.Blocked, _fixture.Ticket(affected[1]).Status);
        Assert.Equal(FrontierOutcome.Reconciled, results[1].Outcome);
        Assert.Equal([other[1]], results[1].Dispatched);
        Assert.Equal(TicketRunStatus.Implementing, _fixture.Ticket(other[1]).Status);
        _fixture.Labels.LookupFailures.Remove(repository);

        FrontierResult recovered = await _fixture.ReconcileAsync(affected.Id);

        Assert.Equal([affected[1]], recovered.Dispatched);
        Assert.Empty(recovered.LookupErrors);
    }

    [Fact]
    public async Task event_bus_diagnostics_are_reported_after_other_repositories_have_been_reconciled()
    {
        SeededSpec affected = await _fixture.SeedRunningSpecAsync("app", (1, []));
        SeededSpec other = await _fixture.SeedRunningSpecAsync("other", (1, []));
        _fixture.Labels.LookupFailures[new GitHubRepoRef("octo", "app")] =
            new RepositoryLabelException("Check Issues permissions and retry.");

        AggregateException error = await Assert.ThrowsAsync<AggregateException>(() =>
            _fixture.HandleAsync(new TicketRunStatusChanged(other.Id, other[1], TicketRunStatus.Implementing, TicketRunStatus.Reviewing, TicketExecutionFixture.T0)));

        Assert.Contains("Check Issues permissions and retry.", Assert.Single(error.InnerExceptions).Message);
        Assert.Equal(TicketRunStatus.Blocked, _fixture.Ticket(affected[1]).Status);
        Assert.Equal(TicketRunStatus.Implementing, _fixture.Ticket(other[1]).Status);
        Assert.Equal([new ImplementationAssignment(other.Id, other[1])], _fixture.Launcher.Launched);
    }

    [Fact]
    public async Task reconciling_a_running_spec_promotes_unblocked_tickets_and_starts_them()
    {
        SeededSpec spec = await _fixture.SeedRunningSpecAsync("app", (1, []), (2, [1]));

        FrontierResult result = await _fixture.ReconcileAsync(spec.Id);

        Assert.Equal(FrontierOutcome.Reconciled, result.Outcome);
        Assert.Equal([spec[1]], result.Unblocked);
        Assert.Equal([spec[1]], result.Dispatched);
        Assert.Equal(TicketRunStatus.Implementing, _fixture.Ticket(spec[1]).Status);
        Assert.Equal(TicketRunStatus.Blocked, _fixture.Ticket(spec[2]).Status);
        Assert.Equal([new ImplementationAssignment(spec.Id, spec[1])], _fixture.Launcher.Launched);
        Assert.Equal(
            [(TicketRunStatus.Blocked, TicketRunStatus.Ready), (TicketRunStatus.Ready, TicketRunStatus.Implementing)],
            _fixture.Db.CommittedEvents.OfType<TicketRunStatusChanged>().Select(changed => (changed.From, changed.To)));
    }

    [Fact]
    public async Task diamond_dag_starts_both_middle_tickets_after_root_integrates_while_others_still_run()
    {
        SeededSpec spec = await _fixture.SeedRunningSpecAsync("app", (1, []), (2, [1]), (3, [1]), (4, [2, 3]), (5, []));
        await _fixture.HandleAsync(new SpecRunStatusChanged(spec.Id, spec.RepositoryId, SpecRunStatus.Preparing, SpecRunStatus.Running, TicketExecutionFixture.T0));
        await _fixture.PumpEventsAsync();
        Assert.Equal([spec[1], spec[5]], _fixture.Launcher.Launched.Select(assignment => assignment.TicketRunId));

        await _fixture.IntegrateAsync(spec, spec[1]);
        await _fixture.PumpEventsAsync();

        Assert.Equal([spec[1], spec[5], spec[2], spec[3]], _fixture.Launcher.Launched.Select(assignment => assignment.TicketRunId));
        Assert.Equal(TicketRunStatus.Implementing, _fixture.Ticket(spec[2]).Status);
        Assert.Equal(TicketRunStatus.Implementing, _fixture.Ticket(spec[3]).Status);
        Assert.Equal(TicketRunStatus.Implementing, _fixture.Ticket(spec[5]).Status);
        Assert.Equal(TicketRunStatus.Blocked, _fixture.Ticket(spec[4]).Status);
    }

    [Fact]
    public async Task bottom_of_the_diamond_starts_only_once_both_middle_tickets_are_integrated()
    {
        SeededSpec spec = await _fixture.SeedRunningSpecAsync("app", (1, []), (2, [1]), (3, [1]), (4, [2, 3]));
        await _fixture.ReconcileAsync(spec.Id);
        await _fixture.IntegrateAsync(spec, spec[1]);
        await _fixture.PumpEventsAsync();

        await _fixture.IntegrateAsync(spec, spec[2]);
        await _fixture.PumpEventsAsync();
        TicketRunStatus whileOneMiddleRuns = _fixture.Ticket(spec[4]).Status;
        await _fixture.IntegrateAsync(spec, spec[3]);
        await _fixture.PumpEventsAsync();

        Assert.Equal(TicketRunStatus.Blocked, whileOneMiddleRuns);
        Assert.Equal(TicketRunStatus.Implementing, _fixture.Ticket(spec[4]).Status);
        Assert.Single(_fixture.Launcher.Launched, assignment => assignment.TicketRunId == spec[4]);
    }

    [Fact]
    public async Task duplicate_frontier_events_launch_exactly_one_implementer()
    {
        SeededSpec spec = await _fixture.SeedRunningSpecAsync("app", (1, []));
        var request = new FrontierReconciliationRequested(spec.Id, TicketExecutionFixture.T0);

        await _fixture.HandleAsync(request);
        await _fixture.HandleAsync(request);
        await _fixture.HandleAsync(new SpecRunStatusChanged(spec.Id, spec.RepositoryId, SpecRunStatus.Preparing, SpecRunStatus.Running, TicketExecutionFixture.T0));
        await _fixture.PumpEventsAsync();

        Assert.Single(_fixture.Launcher.Launched);
        Assert.Single(_fixture.Db.CommittedEvents.OfType<TicketRunStatusChanged>(), changed => changed.To == TicketRunStatus.Implementing);
    }

    [Fact]
    public async Task a_spec_that_is_not_running_is_left_alone()
    {
        SeededSpec spec = await _fixture.SeedRunningSpecAsync("app", (1, []));
        CasWorkflowScope scope = _fixture.Db.OpenScope();
        (await scope.GetAsync(spec.Id, TicketExecutionFixture.Token))!.TransitionTo(SpecRunStatus.ParentReviewing, TicketExecutionFixture.T0);
        await scope.SaveChangesAsync(TicketExecutionFixture.Token);

        FrontierResult result = await _fixture.ReconcileAsync(spec.Id);

        Assert.Equal(FrontierOutcome.NotRunning, result.Outcome);
        Assert.Equal(TicketRunStatus.Blocked, _fixture.Ticket(spec[1]).Status);
        Assert.Empty(_fixture.Launcher.Launched);
    }

    [Fact]
    public async Task a_retried_ticket_is_dispatched_again_when_it_returns_to_ready()
    {
        SeededSpec spec = await _fixture.SeedRunningSpecAsync("app", (1, []));
        await _fixture.ReconcileAsync(spec.Id);
        await _fixture.MoveAsync(spec[1], TicketRunStatus.NeedsAttention);
        _fixture.Db.TakeUndispatchedEvents();

        await _fixture.MoveAsync(spec[1], TicketRunStatus.Ready);
        await _fixture.PumpEventsAsync();

        Assert.Equal(2, _fixture.Launcher.Launched.Count);
        Assert.Equal(TicketRunStatus.Implementing, _fixture.Ticket(spec[1]).Status);
        Assert.Equal(2, _fixture.Ticket(spec[1]).Attempt);
    }

    [Fact]
    public async Task a_locally_skipped_blocker_still_blocks_until_its_github_issue_is_closed()
    {
        SeededSpec spec = await _fixture.SeedRunningSpecAsync("app", (1, []), (2, [1]));
        await _fixture.ReconcileAsync(spec.Id);
        await _fixture.MoveAsync(spec[1], TicketRunStatus.NeedsAttention);
        _fixture.Db.TakeUndispatchedEvents();

        await _fixture.MoveAsync(spec[1], TicketRunStatus.Skipped);
        await _fixture.PumpEventsAsync();

        Assert.Equal(TicketRunStatus.Blocked, _fixture.Ticket(spec[2]).Status);
        Assert.Equal([spec[1]], _fixture.Launcher.Launched.Select(assignment => assignment.TicketRunId));
    }

    [Fact]
    public async Task removing_a_native_relationship_releases_a_ticket_even_when_the_saved_edge_and_open_issue_remain()
    {
        SeededSpec spec = await _fixture.SeedRunningSpecAsync("app", (1, [2]), (2, []));
        await _fixture.MoveAsync(spec[2], TicketRunStatus.NeedsAttention);
        _fixture.Issues.SetBlockedBy(_fixture.Ticket(spec[1]).Issue);

        FrontierResult result = await _fixture.ReconcileAsync(spec.Id);

        Assert.Equal([spec[1]], result.Dispatched);
        Assert.Equal(TicketRunStatus.NeedsAttention, _fixture.Ticket(spec[2]).Status);
    }

    [Fact]
    public async Task a_closed_native_blocker_releases_work_without_local_integration_or_a_merged_pull_request()
    {
        SeededSpec spec = await _fixture.SeedRunningSpecAsync("app", (1, [2]), (2, []));
        await _fixture.MoveAsync(spec[2], TicketRunStatus.NeedsAttention);
        await _fixture.Issues.CloseAsync(_fixture.Ticket(spec[2]).Issue, IssueCloseReason.Completed, TicketExecutionFixture.Token);

        FrontierResult result = await _fixture.ReconcileAsync(spec.Id);

        Assert.Equal([spec[1]], result.Dispatched);
        Assert.Null(_fixture.Ticket(spec[2]).IntegratedCommitSha);
    }

    [Fact]
    public async Task a_ready_ticket_observes_new_cross_repository_blockers_on_every_decision()
    {
        SeededSpec spec = await _fixture.SeedRunningSpecAsync("app", (1, []));
        TicketRun ticket = _fixture.Ticket(spec[1]);
        var blocker = new IssueRef("elsewhere", "library", 1);
        _fixture.Issues.Seed(blocker, "Same number, different repository");
        await _fixture.MoveAsync(ticket.Id, TicketRunStatus.Ready);
        _fixture.Issues.SetBlockedBy(ticket.Issue, blocker);

        FrontierResult blocked = await _fixture.ReconcileAsync(spec.Id);
        Assert.Equal(TicketRunStatus.Blocked, _fixture.Ticket(ticket.Id).Status);
        await _fixture.Issues.CloseAsync(blocker, IssueCloseReason.Completed, TicketExecutionFixture.Token);
        FrontierResult released = await _fixture.ReconcileAsync(spec.Id);

        Assert.Empty(blocked.Dispatched);
        Assert.Equal([ticket.Id], released.Dispatched);
    }

    [Fact]
    public async Task reopening_an_integrated_blockers_issue_blocks_new_implementation()
    {
        SeededSpec spec = await _fixture.SeedRunningSpecAsync("app", (1, []), (2, [1]));
        await _fixture.ReconcileAsync(spec.Id);
        await _fixture.IntegrateAsync(spec, spec[1]);
        _fixture.Issues.Reopen(_fixture.Ticket(spec[1]).Issue);

        FrontierResult result = await _fixture.ReconcileAsync(spec.Id);

        Assert.Empty(result.Dispatched);
        Assert.Equal(TicketRunStatus.Blocked, _fixture.Ticket(spec[2]).Status);
    }

    [Fact]
    public async Task missing_blocker_information_only_defers_the_affected_ticket_not_other_tickets_or_specs()
    {
        SeededSpec spec = await _fixture.SeedRunningSpecAsync("app", (1, []), (2, []));
        SeededSpec other = await _fixture.SeedRunningSpecAsync("other", (1, []));
        var missing = new IssueRef("another-owner", "library", 900);
        _fixture.Issues.SetBlockedBy(_fixture.Ticket(spec[1]).Issue, missing);
        await _fixture.MoveAsync(spec[1], TicketRunStatus.Ready);

        IReadOnlyList<FrontierResult> results = await _fixture.Frontier().ReconcileAllRunningAsync(TicketExecutionFixture.Token);

        Assert.Equal(FrontierOutcome.GitHubUnavailable, results[0].Outcome);
        Assert.Contains("unavailable", Assert.Single(results[0].LookupErrors).Message);
        Assert.Equal(FrontierOutcome.Reconciled, results[1].Outcome);
        Assert.Empty(results[1].LookupErrors);
        Assert.Equal([spec[2]], results[0].Dispatched);
        Assert.Equal([other[1]], results[1].Dispatched);
        Assert.Equal(TicketRunStatus.Blocked, _fixture.Ticket(spec[1]).Status);
        Assert.Equal(TicketRunStatus.Implementing, _fixture.Ticket(spec[2]).Status);
        Assert.Equal(TicketRunStatus.Implementing, _fixture.Ticket(other[1]).Status);
        _fixture.Issues.Seed(missing, "Now available");
        await _fixture.Issues.CloseAsync(missing, IssueCloseReason.Completed, TicketExecutionFixture.Token);

        FrontierResult recovered = await _fixture.ReconcileAsync(spec.Id);

        Assert.Equal([spec[1]], recovered.Dispatched);
        Assert.Empty(recovered.LookupErrors);
    }

    [Fact]
    public async Task an_open_blocker_defers_work_without_reading_further_blockers_until_it_closes()
    {
        SeededSpec spec = await _fixture.SeedRunningSpecAsync("app", (1, []), (2, []));
        var open = new IssueRef("another-owner", "library", 900);
        var missing = new IssueRef("another-owner", "library", 901);
        _fixture.Issues.Seed(open, "Open blocker");
        _fixture.Issues.SetBlockedBy(_fixture.Ticket(spec[1]).Issue, open, missing);

        FrontierResult blocked = await _fixture.ReconcileAsync(spec.Id);

        Assert.Equal(FrontierOutcome.Reconciled, blocked.Outcome);
        Assert.Empty(blocked.LookupErrors);
        Assert.Equal([spec[2]], blocked.Dispatched);
        await _fixture.Issues.CloseAsync(open, IssueCloseReason.Completed, TicketExecutionFixture.Token);

        FrontierResult deferred = await _fixture.ReconcileAsync(spec.Id);

        Assert.Equal(FrontierOutcome.GitHubUnavailable, deferred.Outcome);
        Assert.Single(deferred.LookupErrors);
        Assert.Empty(deferred.Dispatched);
        Assert.Equal(TicketRunStatus.Blocked, _fixture.Ticket(spec[1]).Status);
        _fixture.Issues.Seed(missing, "Second blocker");
        await _fixture.Issues.CloseAsync(missing, IssueCloseReason.Completed, TicketExecutionFixture.Token);

        FrontierResult recovered = await _fixture.ReconcileAsync(spec.Id);

        Assert.Equal([spec[1]], recovered.Dispatched);
        Assert.Empty(recovered.LookupErrors);
    }
}
