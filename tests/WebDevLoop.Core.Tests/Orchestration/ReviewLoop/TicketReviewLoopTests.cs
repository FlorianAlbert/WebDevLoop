using System.Text.Json;
using WebDevLoop.Core.Agents;
using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Events;
using WebDevLoop.Core.Orchestration.ReviewLoop;
using WebDevLoop.Core.Orchestration.Results;
using WebDevLoop.Core.Orchestration.TicketExecution;
using WebDevLoop.Core.Tests.Orchestration.TicketExecution;

namespace WebDevLoop.Core.Tests.Orchestration.ReviewLoop;

public sealed class TicketReviewLoopTests
{
    private const string ReviewerTemplate =
        "{review_axis}|{review_scope}|{worktree_path}|{branch_name}|{diff_base_ref}|{diff_head_ref}|{changed_files}|{review_iteration}";

    private readonly ReviewLoopFixture _fixture = new();

    [Fact]
    public async Task both_clean_reviews_move_the_ticket_to_integrating()
    {
        SeededSpec spec = await _fixture.SeedReviewingAsync(1);
        _fixture.Clean(FindingAxis.CodingStandards).Clean(FindingAxis.Specification);

        ReviewLoopResult result = await _fixture.RunLoopAsync(spec, 1);

        Assert.Equal(ReviewLoopResult.Integrating, result);
        Assert.Equal(TicketRunStatus.Integrating, _fixture.Ticket(spec[1]).Status);
        IReadOnlyList<StepRun> reviews = _fixture.Steps(spec[1], StepKind.Review);
        Assert.Equal(
            [(AgentRole.ReviewerCodingStandards, StepStatus.Succeeded), (AgentRole.ReviewerSpecification, StepStatus.Succeeded)],
            reviews.Select(step => (step.AgentRole!.Value, step.Status)));
        Assert.Contains(_fixture.TicketTransitions(spec[1]), changed => (changed.From, changed.To) == (TicketRunStatus.Reviewing, TicketRunStatus.Integrating));
        Assert.Empty(_fixture.Agents.Resumed);
    }

    [Fact]
    public async Task reviewers_run_as_two_independent_sessions_against_the_implementer_branch()
    {
        _fixture.UseTemplate(AgentRole.ReviewerCodingStandards, ReviewerTemplate);
        _fixture.UseTemplate(AgentRole.ReviewerSpecification, ReviewerTemplate);
        SeededSpec spec = await _fixture.SeedReviewingAsync(1);
        _fixture.Clean(FindingAxis.CodingStandards).Clean(FindingAxis.Specification);
        TicketRun ticket = _fixture.Ticket(spec[1]);
        CommitSha integrationTip = _fixture.Spec(spec.Id).IntegrationTipSha!.Value;
        CommitSha head = ticket.LastImplementedSha!.Value;
        string changedFile = Assert.Single(await _fixture.Git.GetChangedFilesAsync(spec.Location, integrationTip, head, ReviewLoopFixture.Token));

        await _fixture.RunLoopAsync(spec, 1);

        AgentRunRequest[] requests = _fixture.ReviewerRequests.OrderBy(request => request.Role).ToArray();
        Assert.Equal([AgentRole.ReviewerCodingStandards, AgentRole.ReviewerSpecification], requests.Select(request => request.Role));
        Assert.NotEqual(requests[0].SessionId, requests[1].SessionId);
        string context = $"ticket|{ticket.WorktreePath}|{ticket.BranchName}|{integrationTip}|{head}|{changedFile}|0";
        Assert.Equal($"coding_standards|{context}", requests[0].Prompt);
        Assert.Equal($"specification|{context}", requests[1].Prompt);
        Assert.All(requests, request => Assert.Equal(ticket.WorktreePath, request.Policy.Paths.WorkingDirectory));
        Assert.Equal(
            requests.Select(request => (request.StepRunId, request.SessionId.Value)),
            _fixture.Steps(spec[1], StepKind.Review).Select(step => (step.Id, step.CopilotSessionId!)));
    }

    [Fact]
    public async Task reviewer_policy_denies_write_and_mutation_tools()
    {
        SeededSpec spec = await _fixture.SeedReviewingAsync(1);
        _fixture.Clean(FindingAxis.CodingStandards).Clean(FindingAxis.Specification);

        await _fixture.RunLoopAsync(spec, 1);

        AgentRunRequest[] requests = _fixture.ReviewerRequests.ToArray();
        Assert.Equal(2, requests.Length);
        Assert.All(requests, request =>
        {
            RoleCapabilityPolicy policy = request.Policy;
            Assert.False(policy.Allows(AgentCapability.WriteFiles));
            Assert.False(policy.Allows(AgentCapability.CreateLocalCommit));
            Assert.False(policy.Allows(AgentCapability.PushRefs));
            Assert.False(policy.Allows(AgentCapability.ManageIssues));
            Assert.False(policy.IsCommandAllowed("git commit -am wip"));
            Assert.False(policy.IsCommandAllowed("git push origin HEAD"));
            Assert.False(policy.IsCommandAllowed("gh pr create"));
            Assert.True(policy.IsCommandAllowed("git diff main...HEAD"));
            Assert.Empty(policy.Paths.WritableRoots);
            Assert.Equal(GitHubTokenAccess.None, policy.TokenAccess);
        });
    }

    [Fact]
    public async Task one_axis_finding_triggers_a_fix_and_both_reviews_rerun()
    {
        SeededSpec spec = await _fixture.SeedReviewingAsync(1);
        _fixture.Clean(FindingAxis.CodingStandards).Issues(FindingAxis.Specification, ReviewLoopFixture.SpecFinding)
            .Fix()
            .Clean(FindingAxis.CodingStandards).Clean(FindingAxis.Specification);

        ReviewLoopResult result = await _fixture.RunLoopAsync(spec, 1);

        Assert.Equal(ReviewLoopResult.Integrating, result);
        TicketRun ticket = _fixture.Ticket(spec[1]);
        Assert.Equal(TicketRunStatus.Integrating, ticket.Status);
        Assert.Equal(1, ticket.ReviewIteration);
        StepRun fix = Assert.Single(_fixture.Steps(spec[1], StepKind.Fix));
        Assert.Equal(StepStatus.Succeeded, fix.Status);
        Assert.Equal(4, _fixture.Steps(spec[1], StepKind.Review).Count(step => step.Status == StepStatus.Succeeded));
        AgentRunRequest[] secondRound = _fixture.ReviewerRequests.Skip(2).ToArray();
        Assert.Equal([AgentRole.ReviewerCodingStandards, AgentRole.ReviewerSpecification], secondRound.Select(request => request.Role).Order());
        Assert.Equal(
            [
                (TicketRunStatus.Reviewing, TicketRunStatus.FixingReviewFindings),
                (TicketRunStatus.FixingReviewFindings, TicketRunStatus.Reviewing),
                (TicketRunStatus.Reviewing, TicketRunStatus.Integrating),
            ],
            _fixture.TicketTransitions(spec[1]).Select(changed => (changed.From, changed.To)).SkipWhile(transition => transition.To != TicketRunStatus.Reviewing).Skip(1));
    }

    [Fact]
    public async Task fix_resumes_the_original_implementer_session_with_the_findings()
    {
        _fixture.UseTemplate(AgentRole.Implementer, "{review_iteration}/{max_review_iterations}\n{review_findings_json}");
        SeededSpec spec = await _fixture.SeedReviewingAsync(1);
        _fixture.Issues(FindingAxis.CodingStandards, ReviewLoopFixture.StandardsFinding).Issues(FindingAxis.Specification, ReviewLoopFixture.SpecFinding)
            .Fix()
            .Clean(FindingAxis.CodingStandards).Clean(FindingAxis.Specification);
        StepRun implement = Assert.Single(_fixture.Steps(spec[1], StepKind.Implement));

        await _fixture.RunLoopAsync(spec, 1);

        AgentRunRequest resumed = Assert.Single(_fixture.Agents.Resumed);
        Assert.Equal(implement.CopilotSessionId, resumed.SessionId.Value);
        Assert.Equal(AgentRole.Implementer, resumed.Role);
        Assert.Equal(_fixture.Ticket(spec[1]).WorktreePath, resumed.Policy.Paths.WorkingDirectory);
        StepRun fix = Assert.Single(_fixture.Steps(spec[1], StepKind.Fix));
        Assert.Equal((implement.CopilotSessionId, resumed.StepRunId), (fix.CopilotSessionId, fix.Id));
        string[] lines = resumed.Prompt.Split('\n', 2);
        Assert.Equal("1/5", lines[0]);
        JsonElement[] findings = JsonDocument.Parse(lines[1]).RootElement.EnumerateArray().ToArray();
        Assert.Equal(["cs-1", "spec-1"], findings.Select(finding => finding.GetProperty("id").GetString()));
        Assert.Equal(["coding_standards", "specification"], findings.Select(finding => finding.GetProperty("axis").GetString()));
        Assert.Equal(12, findings[0].GetProperty("line").GetInt32());
        Assert.Equal("Name the constant.", findings[0].GetProperty("recommendation").GetString());
        Assert.Equal("Errors must be logged.", findings[1].GetProperty("spec_reference").GetString());
    }

    [Fact]
    public async Task exceeding_max_review_iterations_marks_the_ticket_needs_attention()
    {
        SeededSpec spec = await _fixture.SeedReviewingAsync(1);
        _fixture.Settings.Configure(spec.RepositoryId, settings => settings with { MaxReviewIterations = 2 });
        _fixture.Clean(FindingAxis.CodingStandards).Issues(FindingAxis.Specification, ReviewLoopFixture.SpecFinding)
            .Fix()
            .Clean(FindingAxis.CodingStandards).Issues(FindingAxis.Specification, ReviewLoopFixture.SpecFinding)
            .Fix("never.cs");

        ReviewLoopResult result = await _fixture.RunLoopAsync(spec, 1);

        Assert.Equal(ReviewLoopOutcome.ReviewIterationsExhausted, result.Outcome);
        TicketRun ticket = _fixture.Ticket(spec[1]);
        Assert.Equal(TicketRunStatus.NeedsAttention, ticket.Status);
        Assert.Equal(result.Reason, ticket.FailureReason);
        Assert.Contains("2 review round(s)", result.Reason);
        Assert.Single(_fixture.Agents.Resumed);
        Assert.Equal(4, _fixture.Steps(spec[1], StepKind.Review).Count);
    }

    [Fact]
    public async Task fix_turn_waits_for_a_free_implementer_slot_and_is_relaunched_when_one_frees()
    {
        SeededSpec spec = await _fixture.SeedReviewingAsync(1, 2);
        _fixture.Settings.Configure(spec.RepositoryId, settings => settings with { MaxConcurrentImplementersPerRepo = 1 });
        await _fixture.Execution.MoveAsync(spec[2], TicketRunStatus.FixingReviewFindings);
        _fixture.Db.TakeUndispatchedEvents();
        _fixture.Clean(FindingAxis.CodingStandards).Issues(FindingAxis.Specification, ReviewLoopFixture.SpecFinding)
            .Fix()
            .Clean(FindingAxis.CodingStandards).Clean(FindingAxis.Specification);

        ReviewLoopResult waiting = await _fixture.RunLoopAsync(spec, 1);
        await _fixture.Execution.MoveAsync(spec[2], TicketRunStatus.Reviewing);
        await _fixture.DeliverEventsAsync();
        ReviewLoopResult resumed = await _fixture.RunLoopAsync(spec, 1);

        Assert.Equal(ReviewLoopResult.AwaitingImplementerCapacity, waiting);
        Assert.Equal([new ReviewAssignment(spec.Id, spec[1])], _fixture.Launcher.Launched);
        Assert.Equal(ReviewLoopResult.Integrating, resumed);
        Assert.Single(_fixture.Agents.Resumed);
        Assert.Equal(4, _fixture.Steps(spec[1], StepKind.Review).Count);
    }

    [Fact]
    public async Task fix_turn_claims_its_implementer_slot_under_the_capacity_gate()
    {
        SeededSpec spec = await _fixture.SeedReviewingAsync(1);
        _fixture.Clean(FindingAxis.CodingStandards).Issues(FindingAxis.Specification, ReviewLoopFixture.SpecFinding)
            .Fix()
            .Clean(FindingAxis.CodingStandards).Clean(FindingAxis.Specification);

        Task<ReviewLoopResult> loop;
        using (await _fixture.Gate.EnterAsync(ReviewLoopFixture.Token))
        {
            loop = _fixture.RunLoopAsync(spec, 1);
            await Task.Yield();
            Assert.False(loop.IsCompleted);
            Assert.Equal(TicketRunStatus.Reviewing, _fixture.Ticket(spec[1]).Status);
            Assert.Empty(_fixture.Agents.Resumed);
        }

        Assert.Equal(ReviewLoopResult.Integrating, await loop);
    }

    [Fact]
    public async Task fix_report_with_unexpected_sha_marks_the_ticket_needs_attention()
    {
        SeededSpec spec = await _fixture.SeedReviewingAsync(1);
        _fixture.Clean(FindingAxis.CodingStandards).Issues(FindingAxis.Specification, ReviewLoopFixture.SpecFinding);
        _fixture.Agents.Script(AgentRole.Implementer, request =>
        {
            CommitSha head = _fixture.Git.CommitInWorktree(request.Policy.Paths.WorkingDirectory, "fix.cs");
            return ImplementationReport.Completed(_fixture.Git.Commit([head], "elsewhere.cs"), "Fixed.");
        });

        ReviewLoopResult result = await _fixture.RunLoopAsync(spec, 1);

        Assert.Equal(ReviewLoopOutcome.Failed, result.Outcome);
        Assert.Equal(TicketRunStatus.NeedsAttention, _fixture.Ticket(spec[1]).Status);
        Assert.Equal(StepStatus.Failed, Assert.Single(_fixture.Steps(spec[1], StepKind.Fix)).Status);
        Assert.Equal(2, _fixture.Steps(spec[1], StepKind.Review).Count);
    }

    [Fact]
    public async Task failed_resumed_fix_turn_is_retried_in_a_fresh_implementer_session()
    {
        SeededSpec spec = await _fixture.SeedReviewingAsync(1);
        _fixture.Clean(FindingAxis.CodingStandards).Issues(FindingAxis.Specification, ReviewLoopFixture.SpecFinding);
        _fixture.Agents.Script(AgentRole.Implementer, _ => throw new InvalidAgentReportException("Session could not be resumed."));
        _fixture.Fix().Clean(FindingAxis.CodingStandards).Clean(FindingAxis.Specification);
        string original = Assert.Single(_fixture.Steps(spec[1], StepKind.Implement)).CopilotSessionId!;

        ReviewLoopResult result = await _fixture.RunLoopAsync(spec, 1);

        Assert.Equal(ReviewLoopResult.Integrating, result);
        Assert.Equal(original, Assert.Single(_fixture.Agents.Resumed).SessionId.Value);
        AgentRunRequest fresh = Assert.Single(_fixture.Agents.Started, request => request.Role == AgentRole.Implementer);
        Assert.NotEqual(original, fresh.SessionId.Value);
        Assert.Equal([StepStatus.Failed, StepStatus.Succeeded], _fixture.Steps(spec[1], StepKind.Fix).Select(step => step.Status));
    }

    [Fact]
    public async Task reviewer_failing_every_attempt_marks_the_ticket_needs_attention()
    {
        SeededSpec spec = await _fixture.SeedReviewingAsync(1);
        _fixture.Settings.Configure(spec.RepositoryId, settings => settings with { MaxRetries = 1 });
        _fixture.Clean(FindingAxis.CodingStandards);

        ReviewLoopResult result = await _fixture.RunLoopAsync(spec, 1);

        Assert.Equal(ReviewLoopOutcome.Failed, result.Outcome);
        Assert.Contains("specification", result.Reason, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(TicketRunStatus.NeedsAttention, _fixture.Ticket(spec[1]).Status);
        Assert.Equal(
            [
                (AgentRole.ReviewerCodingStandards, StepStatus.Succeeded),
                (AgentRole.ReviewerSpecification, StepStatus.Failed),
                (AgentRole.ReviewerSpecification, StepStatus.Failed),
            ],
            _fixture.Steps(spec[1], StepKind.Review).Select(step => (step.AgentRole!.Value, step.Status)));
    }

    [Fact]
    public async Task duplicate_loops_run_one_review_round()
    {
        SeededSpec spec = await _fixture.SeedReviewingAsync(1);
        _fixture.Clean(FindingAxis.CodingStandards).Clean(FindingAxis.Specification);
        _fixture.Db.HoldSavesUntil(parties: 2);

        ReviewLoopResult[] results = await Task.WhenAll(_fixture.RunLoopAsync(spec, 1), _fixture.RunLoopAsync(spec, 1));

        Assert.Equal([ReviewLoopOutcome.Integrating, ReviewLoopOutcome.ConcurrencyConflict], results.Select(result => result.Outcome).Order());
        Assert.Equal(2, _fixture.ReviewerRequests.Count());
        Assert.Equal(2, _fixture.Steps(spec[1], StepKind.Review).Count);
    }

    [Fact]
    public async Task partly_reviewed_round_resumes_with_only_the_missing_axis()
    {
        _fixture.UseTemplate(AgentRole.Implementer, "{review_findings_json}");
        SeededSpec spec = await _fixture.SeedReviewingAsync(1);
        TicketRun ticket = _fixture.Ticket(spec[1]);
        var target = new ReviewTarget(ticket.WorktreePath!, ticket.BranchName, _fixture.Spec(spec.Id).IntegrationTipSha!.Value, ticket.LastImplementedSha!.Value);
        _fixture.Issues(FindingAxis.CodingStandards, ReviewLoopFixture.StandardsFinding);
        await _fixture.Reviews().RunAsync(
            new ReviewRequest(spec.Id, ticket.Id, ReviewScope.Ticket, target, new ReviewRound(ticket.Attempt, 0), [FindingAxis.CodingStandards]),
            ReviewLoopFixture.Token);
        _fixture.Clean(FindingAxis.Specification).Fix().Clean(FindingAxis.CodingStandards).Clean(FindingAxis.Specification);

        ReviewLoopResult result = await _fixture.RunLoopAsync(spec, 1);

        Assert.Equal(ReviewLoopResult.Integrating, result);
        Assert.Equal(
            [AgentRole.ReviewerCodingStandards, AgentRole.ReviewerSpecification, AgentRole.ReviewerCodingStandards, AgentRole.ReviewerSpecification],
            _fixture.ReviewerRequests.Select(request => request.Role).Take(2).Concat(_fixture.ReviewerRequests.Skip(2).Select(request => request.Role).Order()));
        Assert.Contains("\"cs-1\"", Assert.Single(_fixture.Agents.Resumed).Prompt);
    }

    [Fact]
    public async Task loop_for_a_ticket_that_is_not_reviewing_does_nothing()
    {
        SeededSpec spec = await _fixture.Execution.SeedRunningSpecAsync("app", (1, []));

        ReviewLoopResult result = await _fixture.RunLoopAsync(spec, 1);

        Assert.Equal(ReviewLoopResult.NotReviewing, result);
        Assert.Empty(_fixture.Agents.Started);
        Assert.Equal(TicketRunStatus.Blocked, _fixture.Ticket(spec[1]).Status);
    }
}
