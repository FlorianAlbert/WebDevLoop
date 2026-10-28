using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Orchestration.SpecQueue;
using WebDevLoop.Core.Ports;
using WebDevLoop.Core.Queries;
using WebDevLoop.Infrastructure.Queries;
using WebDevLoop.Infrastructure.Tests.Persistence;
using WebDevLoop.Infrastructure.Tests.GitHub.Issues;
using static WebDevLoop.Infrastructure.Tests.GitHub.Issues.FakeIssuesApiHandler;

namespace WebDevLoop.Infrastructure.Tests.Queries;

public sealed class EfQueriesTests : GitHubIssuesTestBase, IDisposable
{
    private static readonly CommitSha LayerSha = new(new string('c', 40));

    private readonly PersistenceHarness _harness = new();

    public void Dispose() => _harness.Dispose();

    [Fact]
    public async Task repositories_are_listed_and_fetched_as_views()
    {
        int firstId;
        using (PersistenceScope write = _harness.OpenScope())
        {
            RepositoryRecord first = TestData.NewRepository("acme", "widgets");
            first.SetEnabled(true, TestData.Now);
            write.Repositories.Add(first);
            write.Repositories.Add(TestData.NewRepository("acme", "gadgets"));
            await write.SaveAsync();
            firstId = first.Id;
        }

        using PersistenceScope read = _harness.OpenScope();
        var queries = new EfRepositoryQueries(read.Context);

        IReadOnlyList<RepositoryView> all = await queries.ListAsync(CancellationToken.None);
        RepositoryView? one = await queries.GetAsync(firstId, CancellationToken.None);

        Assert.Equal(["widgets", "gadgets"], all.Select(view => view.Name));
        Assert.Equal(("acme", "widgets", "main", "https://github.com/acme/widgets.git", "/work/acme/widgets", true), (one!.Owner, one.Name, one.DefaultBaseBranch, one.CloneUrl, one.LocalPath, one.IsEnabled));
        Assert.Null(await queries.GetAsync(firstId + 100, CancellationToken.None));
        Assert.Empty(read.Context.ChangeTracker.Entries());
    }

    [Fact]
    public async Task spec_runs_are_listed_by_queue_position_and_fetched_by_id()
    {
        (int repositoryId, _) = await TestData.SeedSpecRunAsync(_harness, "run-1");
        using (PersistenceScope write = _harness.OpenScope())
        {
            write.SpecRuns.Add(TestData.NewSpecRun(repositoryId, "run-0", issueNumber: 20, queuePosition: 0));
            await write.SaveAsync();
        }

        using PersistenceScope read = _harness.OpenScope();
        var queries = new EfRunQueries(read.Context);

        IReadOnlyList<SpecRunView> runs = await queries.ListSpecRunsAsync(repositoryId, CancellationToken.None);
        SpecRunView? run = await queries.GetSpecRunAsync(new RunId("run-1"), CancellationToken.None);

        Assert.Equal(["run-0", "run-1"], runs.Select(view => view.Id));
        Assert.Equal((repositoryId, 10, "Spec title", SpecRunStatus.Queued, "webdevloop/run-1/integration"), (run!.RepositoryId, run.ParentIssueNumber, run.Title, run.Status, run.IntegrationBranch));
        Assert.Null(await queries.GetSpecRunAsync(new RunId("missing"), CancellationToken.None));
        Assert.Empty(await queries.ListSpecRunsAsync(repositoryId + 1, CancellationToken.None));
        Assert.Empty(read.Context.ChangeTracker.Entries());
    }

    [Fact]
    public async Task ticket_runs_carry_their_blocking_tickets()
    {
        (RunId specRunId, TicketRunId firstTicket) = await TestData.SeedTicketRunAsync(_harness);
        TicketRunId secondTicket;
        using (PersistenceScope write = _harness.OpenScope())
        {
            TicketRun second = TestData.NewTicketRun(specRunId, "t-2", 12);
            write.Tickets.Add(second);
            await write.SaveAsync();
            write.Tickets.AddDependency(TicketDependency.Create(specRunId, second.Id, firstTicket, DependencySource.GitHub));
            await write.SaveAsync();
            secondTicket = second.Id;
        }

        using PersistenceScope read = _harness.OpenScope();
        var queries = new EfRunQueries(read.Context);

        IReadOnlyList<TicketRunView> tickets = await queries.ListTicketRunsAsync(specRunId, CancellationToken.None);
        TicketRunView? second2 = await queries.GetTicketRunAsync(secondTicket, CancellationToken.None);
        TicketRunView? first = await queries.GetTicketRunAsync(firstTicket, CancellationToken.None);

        Assert.Equal(["t-1", "t-2"], tickets.Select(view => view.Id));
        Assert.Empty(tickets[0].BlockedByTicketRunIds);
        Assert.Equal(["t-1"], tickets[1].BlockedByTicketRunIds);
        Assert.Equal((12, "t-1"), (second2!.IssueNumber, Assert.Single(second2.BlockedByTicketRunIds)));
        Assert.Empty(first!.BlockedByTicketRunIds);
        Assert.Null(await queries.GetTicketRunAsync(new TicketRunId("nope"), CancellationToken.None));
        Assert.Empty(read.Context.ChangeTracker.Entries());
    }

    [Fact]
    public async Task the_latest_integration_saga_of_a_ticket_is_exposed_as_a_view()
    {
        (RunId specRunId, TicketRunId ticketId) = await TestData.SeedTicketRunAsync(_harness);
        using (PersistenceScope write = _harness.OpenScope())
        {
            IntegrationSaga completed = IntegrationSaga.Start(specRunId, ticketId, TestData.Sha1, TestData.Now);
            completed.AdvanceTo(IntegrationSagaCheckpoint.Completed, TestData.Now);
            write.Sagas.Add(completed);
            await write.SaveAsync();
        }

        using (PersistenceScope write = _harness.OpenScope())
        {
            IntegrationSaga latest = IntegrationSaga.Start(specRunId, ticketId, TestData.Sha2, TestData.Now.AddHours(1));
            latest.PullRequestNumber = new PullRequestNumber(9);
            latest.AdvanceTo(IntegrationSagaCheckpoint.PrCreated, TestData.Now.AddHours(2));
            latest.RecordError("stack link failed", TestData.Now.AddHours(3));
            write.Sagas.Add(latest);
            await write.SaveAsync();
        }

        using PersistenceScope read = _harness.OpenScope();
        var queries = new EfRunQueries(read.Context);

        IntegrationSagaView? one = await queries.GetLatestSagaAsync(ticketId, CancellationToken.None);
        IReadOnlyDictionary<string, IntegrationSagaView> perTicket = await queries.ListLatestSagasAsync(specRunId, CancellationToken.None);

        Assert.Equal(
            (ticketId.Value, IntegrationSagaCheckpoint.PrCreated, RunScopedNaming.StackBranch(specRunId, ticketId).Value, 9, "stack link failed", TestData.Now.AddHours(3)),
            (one!.TicketRunId, one.Checkpoint, one.StackBranch, one.PullRequestNumber, one.LastError, one.UpdatedAt));
        Assert.Equal(one, perTicket[ticketId.Value]);
        Assert.Single(perTicket);
        Assert.Null(await queries.GetLatestSagaAsync(new TicketRunId("none"), CancellationToken.None));
        Assert.Empty(await queries.ListLatestSagasAsync(new RunId("missing"), CancellationToken.None));
        Assert.Empty(read.Context.ChangeTracker.Entries());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task unavailable_github_does_not_break_the_run_read_model(bool parentUnavailable)
    {
        (_, RunId runId) = await TestData.SeedSpecRunAsync(_harness, "run-1");
        if (!parentUnavailable)
        {
            Api.Get(IssuePath(10), Issue(10, 100));
            Api.Get(BlockedByPath(10), new[] { Issue(7, 70) });
        }

        int unavailable = parentUnavailable ? 10 : 7;
        Api.Respond(HttpMethod.Get, IssuePath(unavailable), System.Net.HttpStatusCode.ServiceUnavailable, new { message = "GitHub unavailable" });
        using PersistenceScope read = _harness.OpenScope();
        var queries = new EfRunQueries(read.Context);
        var blockers = new ParentBlockerQueries(queries, new EfRepositoryQueries(read.Context), CreateSut());

        ParentBlockersView result = await blockers.GetBlockersAsync(runId, CancellationToken.None);

        Assert.Contains("Blockers unavailable for acme/widgets#10", result.UnavailableReason);
        Assert.Contains("Check issue access", result.UnavailableReason);
        Assert.Contains($"could not read acme/widgets#{unavailable}", result.UnavailableReason);
        Assert.NotNull(await queries.GetSpecRunAsync(runId, CancellationToken.None));

        var gate = new SpecDependencyGate(CreateSut());
        GitHubIssuesException schedulingError = await Assert.ThrowsAsync<GitHubIssuesException>(() =>
            gate.EvaluateAsync(TestData.NewSpecRun(1, runId.Value), CancellationToken.None));

        Assert.Contains(schedulingError.Message, result.UnavailableReason);
    }

    [Fact]
    public async Task blocker_enrichment_is_scoped_deduplicated_and_isolates_failures_from_other_runs()
    {
        (int repositoryId, RunId runId) = await TestData.SeedSpecRunAsync(_harness, "run-1");
        using (PersistenceScope write = _harness.OpenScope())
        {
            write.SpecRuns.Add(TestData.NewSpecRun(repositoryId, "run-2", issueNumber: 20, queuePosition: 2));
            SpecRun finished = TestData.NewSpecRun(repositoryId, "run-finished", issueNumber: 30, queuePosition: 3);
            finished.TransitionTo(SpecRunStatus.Aborted, TestData.Now);
            write.SpecRuns.Add(finished);
            write.SpecRuns.Add(TestData.NewSpecRun(repositoryId, "run-unavailable", issueNumber: 40, queuePosition: 4));
            await write.SaveAsync();
        }

        foreach (int number in new[] { 10, 20 })
        {
            Api.Get(IssuePath(number), Issue(number, number * 10));
            Api.Get(BlockedByPath(number), new[] { Issue(7, 70, repo: "other") });
        }

        const string externalPath = "/repos/acme/other/issues/7";
        Api.Get(externalPath, Issue(7, 70, state: "closed", repo: "other"));
        Api.Get($"{externalPath}/dependencies/blocked_by", Array.Empty<object>());
        using PersistenceScope read = _harness.OpenScope();
        var queries = new ParentBlockerQueries(new EfRunQueries(read.Context), new EfRepositoryQueries(read.Context), CreateSut());

        ParentBlockersView detail = await queries.GetBlockersAsync(runId, CancellationToken.None);

        Assert.True(Assert.Single(detail.Blockers).IsSatisfied);
        Assert.Null(detail.UnavailableReason);
        Assert.DoesNotContain(Api.Requests, request => request.Path is "/repos/acme/widgets/issues/20" or "/repos/acme/widgets/issues/30" or "/repos/acme/widgets/issues/40");
        Api.Requests.Clear();

        IReadOnlyDictionary<string, ParentBlockersView> queue = await queries.ListBlockersAsync(repositoryId, CancellationToken.None);

        Assert.True(Assert.Single(queue["run-1"].Blockers).IsSatisfied);
        Assert.True(Assert.Single(queue["run-2"].Blockers).IsSatisfied);
        Assert.Contains("could not read acme/widgets#40", queue["run-unavailable"].UnavailableReason);
        Assert.False(queue.ContainsKey("run-finished"));
        Assert.Single(Api.RequestsTo(HttpMethod.Get, externalPath));
        Assert.DoesNotContain(Api.Requests, request => request.Path == IssuePath(30));

        Api.Requests.Clear();
        ParentBlockersView terminal = await queries.GetBlockersAsync(new RunId("run-finished"), CancellationToken.None);
        Assert.False(terminal.Evaluated);
        Assert.Empty(Api.Requests);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task blocker_presentation_and_scheduling_agree_independently_of_tracked_run_status(bool allClosed)
    {
        (int repositoryId, _) = await TestData.SeedSpecRunAsync(_harness, "run-1");
        using (PersistenceScope write = _harness.OpenScope())
        {
            SpecRun blocked = TestData.NewSpecRun(repositoryId, "run-2", issueNumber: 20, queuePosition: 2);
            SpecRun merged = TestData.NewSpecRun(repositoryId, "run-3", issueNumber: 30, queuePosition: 3);
            SpecRun running = (await write.SpecRuns.GetAsync(new RunId("run-1"), CancellationToken.None))!;
            foreach (SpecRunStatus next in new[] { SpecRunStatus.Preparing, SpecRunStatus.Running })
            {
                running.TransitionTo(next, TestData.Now);
            }

            foreach (SpecRunStatus next in new[] { SpecRunStatus.Preparing, SpecRunStatus.Running, SpecRunStatus.ParentReviewing, SpecRunStatus.Testing, SpecRunStatus.Completed })
            {
                merged.TransitionTo(next, TestData.Now);
            }

            write.SpecRuns.Add(blocked);
            write.SpecRuns.Add(merged);
            await write.SaveAsync();
        }

        using PersistenceScope read = _harness.OpenScope();
        Api.Get(IssuePath(20), Issue(20, 200));
        Api.Get(BlockedByPath(20), new[] { Issue(10, 100), Issue(30, 300), Issue(6, 600) });
        foreach (int number in new[] { 10, 30, 6 })
        {
            Api.Get(IssuePath(number), Issue(number, number * 10, "Spec title", state: number == 10 || allClosed ? "closed" : "open"));
            Api.Get(BlockedByPath(number), Array.Empty<object>());
        }

        var queries = new ParentBlockerQueries(new EfRunQueries(read.Context), new EfRepositoryQueries(read.Context), CreateSut());

        ParentBlockersView blockers = await queries.GetBlockersAsync(new RunId("run-2"), CancellationToken.None);
        IReadOnlyDictionary<string, ParentBlockersView> byRun = await queries.ListBlockersAsync(repositoryId, CancellationToken.None);

        Assert.Equal(
            [
                new ParentBlockerView("run-1", "acme/widgets#10", 10, "Spec title", SpecRunStatus.Running, true),
                new ParentBlockerView("run-3", "acme/widgets#30", 30, "Spec title", SpecRunStatus.Completed, allClosed),
                new ParentBlockerView(null, "acme/widgets#6", 6, "Spec title", null, allClosed),
            ],
            blockers.Blockers);
        Assert.Equal(blockers.Blockers, byRun["run-2"].Blockers);
        Assert.Equal(2, byRun.Count);
        Assert.False(byRun.ContainsKey("run-3"));
        Assert.Empty((await queries.GetBlockersAsync(new RunId("run-1"), CancellationToken.None)).Blockers);
        var gate = new SpecDependencyGate(CreateSut());
        SpecStartDecision decision = await gate.EvaluateAsync(
            TestData.NewSpecRun(repositoryId, "run-2", issueNumber: 20), CancellationToken.None);
        Assert.Equal(allClosed ? SpecStartKind.FromTrunk : SpecStartKind.Wait, decision.Kind);
        Assert.Empty(read.Context.ChangeTracker.Entries());
    }

    [Fact]
    public async Task steps_of_a_ticket_expose_status_session_and_structured_result()
    {
        (RunId specRunId, TicketRunId ticketId) = await TestData.SeedTicketRunAsync(_harness);
        using (PersistenceScope write = _harness.OpenScope())
        {
            StepRun implement = TestData.NewStep(specRunId, ticketId, "s-1", StepKind.Implement, AgentRole.Implementer);
            implement.CopilotSessionId = "copilot-1";
            implement.RecordLaunchSettings("gpt-test", "xhigh");
            implement.Start(TestData.Now, TimeSpan.FromMinutes(5));
            implement.Finish(StepStatus.Succeeded, TestData.Now.AddMinutes(1), "{\"ok\":true}");
            write.Steps.Add(implement);
            write.Steps.Add(TestData.NewStep(specRunId, ticketId, "s-2", StepKind.Review, AgentRole.ReviewerCodingStandards));
            write.Steps.Add(TestData.NewStep(specRunId, null, "s-parent", StepKind.ParentReview, AgentRole.ReviewerSpecification));
            await write.SaveAsync();
        }

        using PersistenceScope read = _harness.OpenScope();
        var queries = new EfRunQueries(read.Context);

        IReadOnlyList<StepRunView> steps = await queries.ListStepsAsync(ticketId, CancellationToken.None);
        StepRunView? step = await queries.GetStepAsync(new StepRunId("s-1"), CancellationToken.None);

        Assert.Equal(["s-1", "s-2"], steps.Select(view => view.Id));
        Assert.Equal((StepKind.Implement, AgentRole.Implementer, StepStatus.Succeeded, "copilot-1", "{\"ok\":true}"), (step!.Kind, step.AgentRole, step.Status, step.CopilotSessionId, step.StructuredResultJson));
        Assert.Equal(("gpt-test", "xhigh"), (step.Model, step.ReasoningEffort));
        Assert.Equal((null, null), (steps[1].Model, steps[1].ReasoningEffort));
        Assert.Equal(StepStatus.Pending, steps[1].Status);
        Assert.Null((await queries.GetStepAsync(new StepRunId("s-parent"), CancellationToken.None))!.TicketRunId);
        Assert.Null(await queries.GetStepAsync(new StepRunId("missing"), CancellationToken.None));
        Assert.Empty(read.Context.ChangeTracker.Entries());
    }

    [Fact]
    public async Task run_events_and_stack_layers_are_listed_in_order()
    {
        (RunId specRunId, TicketRunId ticketId) = await TestData.SeedTicketRunAsync(_harness);
        using (PersistenceScope write = _harness.OpenScope())
        {
            write.Events.Add(RunEvent.Create(specRunId, null, "SpecRunStatusChanged", "{\"a\":1}", TestData.Now));
            write.Events.Add(RunEvent.Create(specRunId, ticketId, "TicketRunStatusChanged", "{}", TestData.Now.AddSeconds(1)));
            write.Layers.Add(PullStackLayer.Create(specRunId, ticketId, new BranchName("wdl/run-1/t-1"), LayerSha, new PullRequestNumber(7), new BranchName("main"), 1, TestData.Now));
            await write.SaveAsync();
        }

        using PersistenceScope read = _harness.OpenScope();
        var queries = new EfRunQueries(read.Context);

        IReadOnlyList<RunEventView> events = await queries.ListEventsAsync(specRunId, CancellationToken.None);
        IReadOnlyList<StackLayerView> stack = await queries.ListStackAsync(specRunId, CancellationToken.None);

        Assert.Equal(["SpecRunStatusChanged", "TicketRunStatusChanged"], events.Select(view => view.Type));
        Assert.Equal((null, "t-1"), (events[0].TicketRunId, events[1].TicketRunId));
        StackLayerView layer = Assert.Single(stack);
        Assert.Equal((1, "t-1", 7, "main", true, LayerSha.Value), (layer.Position, layer.TicketRunId, layer.PullRequestNumber, layer.BaseBranch, layer.IsDraft, layer.CommitSha));
        Assert.Empty(await queries.ListEventsAsync(new RunId("other"), CancellationToken.None));
        Assert.Empty(await queries.ListStackAsync(new RunId("other"), CancellationToken.None));
    }
}
