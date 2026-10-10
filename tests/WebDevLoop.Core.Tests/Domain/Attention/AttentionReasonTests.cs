using WebDevLoop.Core.Domain;

namespace WebDevLoop.Core.Tests.Domain.Attention;

public sealed class AttentionReasonTests
{
    private static readonly DateTimeOffset T0 = new(2026, 1, 1, 10, 0, 0, TimeSpan.Zero);

    public static TheoryData<AttentionCode> AllCodes() => [.. Enum.GetValues<AttentionCode>()];

    [Theory]
    [MemberData(nameof(AllCodes))]
    public void every_reason_code_has_a_summary_why_it_matters_an_action_and_a_way_forward(AttentionCode code)
    {
        AttentionReason reason = AttentionSamples.For(code);

        Assert.Equal(code, reason.Code);
        Assert.False(string.IsNullOrWhiteSpace(reason.Summary));
        Assert.False(string.IsNullOrWhiteSpace(reason.WhyItMatters));
        Assert.False(string.IsNullOrWhiteSpace(reason.Details));
        Assert.NotEmpty(reason.Actions);
        Assert.All(reason.Actions, action =>
        {
            Assert.False(string.IsNullOrWhiteSpace(action.Label));
            Assert.False(string.IsNullOrWhiteSpace(action.Consequence));
        });
        Assert.True(reason.AutoFix is not null || reason.UserSteps.Count > 0, $"{code} has neither an automatic fix nor steps for the user.");
        Assert.All(reason.UserSteps, step => Assert.False(string.IsNullOrWhiteSpace(step.Text)));
    }

    [Theory]
    [MemberData(nameof(AllCodes))]
    public void every_reason_code_offers_only_buttons_that_exist_for_its_owner(AttentionCode code)
    {
        AttentionReason reason = AttentionSamples.For(code);

        Assert.Equal(reason.Actions.Count, reason.Actions.Select(action => action.Kind).Distinct().Count());
        Assert.Contains(reason.Actions, action => action.Kind is AttentionActionKind.Abort);
    }

    [Theory]
    [MemberData(nameof(AllCodes))]
    public void every_reason_survives_a_json_round_trip(AttentionCode code)
    {
        AttentionReason reason = AttentionSamples.For(code);

        AttentionReason? parsed = AttentionReason.TryParse(reason.ToJson());

        Assert.NotNull(parsed);
        Assert.Equal(reason.ToJson(), parsed.ToJson());
        Assert.Equal(reason.Cause, parsed.Cause);
        Assert.Equal(reason.Actions.Select(action => action.Label), parsed.Actions.Select(action => action.Label));
    }

    [Fact]
    public void every_reason_code_has_a_factory_in_the_catalogue()
    {
        string catalogue = File.ReadAllText(SourceTree.File("src/WebDevLoop.Core/Domain/Attention/AttentionReasons.cs"));

        string[] missing = [.. Enum.GetNames<AttentionCode>().Where(name => !System.Text.RegularExpressions.Regex.IsMatch(catalogue, $@"AttentionCode\.{name}\b"))];

        Assert.Empty(missing);
    }

    [Fact]
    public void reasons_whose_cause_is_webdevloop_and_that_offer_an_auto_fix_say_what_it_does()
    {
        AttentionReason[] withAutoFix = [.. Enum.GetValues<AttentionCode>().Select(AttentionSamples.For).Where(reason => reason.AutoFix is not null)];

        Assert.NotEmpty(withAutoFix);
        Assert.All(withAutoFix, reason =>
        {
            Assert.Equal(AttentionCause.WebDevLoop, reason.Cause);
            Assert.False(string.IsNullOrWhiteSpace(reason.AutoFix!.Description));
            Assert.True(reason.AutoFixPending);
        });
    }

    [Fact]
    public void the_catalogue_marks_the_auto_rows_of_the_findings_with_an_auto_fix()
    {
        AttentionCode[] auto =
        [
            AttentionCode.WorktreeNotClean,
            AttentionCode.TicketBranchNotBasedOnIntegration,
            AttentionCode.IntegrationTemporaryFailure,
            AttentionCode.IntegrationBranchExists,
            AttentionCode.ExplorationFailed,
            AttentionCode.TicketHasNoChanges,
        ];

        Assert.All(auto, code => Assert.NotNull(AttentionSamples.For(code).AutoFix));
        Assert.All(Enum.GetValues<AttentionCode>().Except(auto), code => Assert.Null(AttentionSamples.For(code).AutoFix));
    }

    [Fact]
    public void a_dirty_worktree_reason_gives_the_user_commands_they_can_copy()
    {
        AttentionReason reason = AttentionReasons.WorktreeNotClean("/work/t1", "feature", "dirty");

        string[] commands = [.. reason.UserSteps.Select(step => step.Command).OfType<string>()];

        Assert.Contains("git -C /work/t1 status", commands);
        Assert.Contains("git -C /work/t1 clean -fdx", commands);
    }

    [Fact]
    public void a_reason_cannot_be_created_without_a_summary()
    {
        Assert.Throws<ArgumentException>(() => Build(summary: " "));
    }

    [Fact]
    public void a_reason_cannot_be_created_without_why_it_matters()
    {
        Assert.Throws<ArgumentException>(() => Build(why: ""));
    }

    [Fact]
    public void a_reason_cannot_be_created_without_a_known_code()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Build(code: (AttentionCode)9999));
    }

    [Fact]
    public void a_reason_cannot_be_created_without_an_action()
    {
        Assert.Throws<ArgumentException>(() => Build(emptyActions: true));
        Assert.Throws<ArgumentException>(() => Build(withoutActions: true));
    }

    [Fact]
    public void incomplete_or_malformed_stored_json_reads_back_as_no_reason()
    {
        Assert.Null(AttentionReason.TryParse(null));
        Assert.Null(AttentionReason.TryParse("not json"));
        Assert.Null(AttentionReason.TryParse("{\"code\":\"BaseBranchMissing\",\"summary\":\"\",\"whyItMatters\":\"x\",\"details\":\"\",\"cause\":\"You\",\"actions\":[]}"));
    }

    [Fact]
    public void adding_attempts_keeps_the_guidance_and_appends_to_what_was_tried()
    {
        AttentionReason reason = AttentionReasons.ImplementationFailed(3, "timed out");

        AttentionReason updated = reason.WithTried("Cleaned the worktree").WithAutoFixAttempted("Cleaning did not help");

        Assert.Equal(reason.Summary, updated.Summary);
        Assert.Equal([.. reason.TriedSoFar, "Cleaned the worktree"], updated.TriedSoFar);
        Assert.True(updated.AutoFix!.Attempted);
        Assert.False(updated.AutoFixPending);
    }

    [Fact]
    public void a_ticket_and_a_run_keep_the_reason_and_its_technical_details_while_they_need_attention()
    {
        AttentionReason reason = AttentionReasons.WorktreeNotClean("/work/t1", "feature", "Worktree is Dirty");
        TicketRun ticket = ReviewingTicket();
        ticket.MarkNeedsAttention(reason, T0);

        Assert.Same(reason, ticket.Attention);
        Assert.Equal("Worktree is Dirty", ticket.FailureReason);
        Assert.Equal(TicketRunStatus.Reviewing, ticket.NeedsAttentionFrom);

        ticket.TransitionTo(TicketRunStatus.Reviewing, T0);

        Assert.Null(ticket.Attention);
        Assert.Null(ticket.FailureReason);
    }

    [Fact]
    public void guidance_can_only_be_updated_while_the_ticket_needs_attention()
    {
        TicketRun ticket = ReviewingTicket();

        Assert.Throws<InvalidOperationException>(() => ticket.UpdateAttention(AttentionSamples.For(AttentionCode.ReviewFailed), T0));
    }

    [Fact]
    public void a_step_cannot_end_as_needs_attention_without_a_reason()
    {
        StepRun step = RunningStep();

        Assert.Throws<ArgumentException>(() => step.Finish(StepStatus.NeedsAttention, T0, failureReason: "free text"));
        Assert.Throws<ArgumentNullException>(() => step.MarkNeedsAttention(null!, T0));
    }

    [Fact]
    public void a_step_that_needs_attention_keeps_its_reason()
    {
        StepRun step = RunningStep();
        AttentionReason reason = AttentionReasons.TicketBranchNotBasedOnIntegration("feature", "does not contain the tip");

        step.MarkNeedsAttention(reason, T0, "{}");

        Assert.Equal(StepStatus.NeedsAttention, step.Status);
        Assert.Same(reason, step.Attention);
        Assert.Equal("does not contain the tip", step.FailureReason);
        Assert.Equal("{}", step.StructuredResultJson);
    }

    [Fact]
    public void marking_needs_attention_only_accepts_a_structured_reason()
    {
        foreach (Type owner in new[] { typeof(SpecRun), typeof(TicketRun), typeof(StepRun) })
        {
            System.Reflection.MethodInfo[] methods = [.. owner.GetMethods().Where(method => method.Name == "MarkNeedsAttention")];

            Assert.NotEmpty(methods);
            Assert.All(methods, method => Assert.Equal(typeof(AttentionReason), method.GetParameters()[0].ParameterType));
        }
    }

    /// <summary>
    /// The guard of requirement E: adding a call that parks work with a free-form string (the old way) fails here. Together with
    /// the signatures above, every way into <c>NeedsAttention</c> has to go through <see cref="AttentionReasons"/> or a value
    /// that came from it.
    /// </summary>
    [Fact]
    public void no_call_site_parks_work_with_a_free_form_string()
    {
        string[] offenders =
        [
            .. SourceTree.Sources("src")
                .Where(source => !source.Path.Contains("/Domain/", StringComparison.Ordinal) && !source.Path.Contains("/Migrations/", StringComparison.Ordinal))
                .SelectMany(source => SourceTree.CallsOf(source.Text, "MarkNeedsAttention", "NeedsAttentionAsync").Select(call => (source.Path, call)))
                .Where(entry => entry.call.TrimStart().StartsWith('"') || entry.call.TrimStart().StartsWith("$\"", StringComparison.Ordinal) || entry.call.Contains("$\"", StringComparison.Ordinal) && !entry.call.Contains("AttentionReasons.", StringComparison.Ordinal))
                .Select(entry => $"{entry.Path}: {entry.call.Trim()}"),
        ];

        Assert.Empty(offenders);
    }

    [Fact]
    public void every_call_that_parks_work_names_a_reason_from_the_catalogue_or_a_value_carrying_one()
    {
        string[] offenders =
        [
            .. SourceTree.Sources("src")
                .Where(source => !source.Path.Contains("/Domain/", StringComparison.Ordinal) && !source.Path.Contains("/Migrations/", StringComparison.Ordinal))
                .SelectMany(source => SourceTree.CallsOf(source.Text, "MarkNeedsAttention", "NeedsAttentionAsync").Select(call => (source.Path, call)))
                .Where(entry => !System.Text.RegularExpressions.Regex.IsMatch(
                    entry.call, @"AttentionReasons\.|[Rr]eason|[Aa]ttention|[Pp]roblem|[Ff]ailure"))
                .Select(entry => $"{entry.Path}: {entry.call.Trim()}"),
        ];

        Assert.Empty(offenders);
    }

    private static AttentionReason Build(
        AttentionCode code = AttentionCode.ImplementationFailed,
        string summary = "Something happened.",
        string why = "It matters.",
        bool withoutActions = false,
        bool emptyActions = false) => new(
            code,
            summary,
            why,
            "details",
            AttentionCause.You,
            null,
            null,
            null,
            withoutActions ? null : emptyActions ? [] : [new AttentionAction(AttentionActionKind.Retry, "Retry", "Runs it again.")]);

    private static TicketRun ReviewingTicket()
    {
        TicketRun ticket = TicketRun.Create(new TicketRunId("t1"), new RunId("r1"), new IssueRef("octo", "app", 3), "Ticket", "body", T0);
        foreach (TicketRunStatus next in new[] { TicketRunStatus.Ready, TicketRunStatus.Implementing, TicketRunStatus.Reviewing })
        {
            ticket.TransitionTo(next, T0);
        }

        return ticket;
    }

    private static StepRun RunningStep()
    {
        StepRun step = StepRun.Create(new StepRunId("s1"), new RunId("r1"), new TicketRunId("t1"), StepKind.Implement, AgentRole.Implementer, 1, "hash");
        step.Start(T0, TimeSpan.FromHours(1));
        return step;
    }
}
