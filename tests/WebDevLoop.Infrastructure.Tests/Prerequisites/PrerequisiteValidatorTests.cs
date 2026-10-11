using WebDevLoop.Core.Ports;
using WebDevLoop.Infrastructure.Prerequisites;

namespace WebDevLoop.Infrastructure.Tests.Prerequisites;

public sealed class PrerequisiteValidatorTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 9, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task reports_every_check_in_registration_order()
    {
        var validator = new PrerequisiteValidator(
        [
            StubCheck.Returning("first", PrerequisiteStatus.Passed),
            StubCheck.Returning("second", PrerequisiteStatus.Warning),
            StubCheck.Returning("third", PrerequisiteStatus.Passed),
        ]);

        PrerequisiteReport report = await validator.ValidateAsync(CancellationToken.None);

        Assert.Equal(["first", "second", "third"], report.Checks.Select(check => check.Name));
    }

    [Fact]
    public async Task agent_models_wait_for_authentication_and_do_not_mask_a_missing_sign_in()
    {
        var authentication = new TaskCompletionSource<PrerequisiteCheck>(TaskCreationOptions.RunContinuationsAsynchronously);
        var validator = new PrerequisiteValidator(
        [
            new StubCheck(GitHubAuthCheck.CheckName, _ => authentication.Task),
            new StubCheck(AgentModelsCheck.CheckName, _ => throw new InvalidOperationException("A Model Catalog error must not mask sign-in."),
                GitHubAuthCheck.CheckName),
        ]);

        Task<PrerequisiteReport> pending = validator.ValidateAsync(CancellationToken.None);
        authentication.SetResult(new PrerequisiteCheck(GitHubAuthCheck.CheckName, PrerequisiteStatus.Failed, "Nobody is signed in to GitHub."));
        PrerequisiteReport report = await pending;

        Assert.Equal(GitHubAuthCheck.CheckName, Assert.Single(report.Checks, check => check.Status == PrerequisiteStatus.Failed).Name);
        PrerequisiteCheck models = Assert.Single(report.Checks, check => check.Name == AgentModelsCheck.CheckName);
        Assert.Equal(PrerequisiteStatus.Warning, models.Status);
        Assert.Contains("GitHub authentication", models.Message);
    }

    [Theory]
    [InlineData(PrerequisiteStatus.Passed, PrerequisiteStatus.Passed)]
    [InlineData(PrerequisiteStatus.Warning, PrerequisiteStatus.Passed)]
    [InlineData(PrerequisiteStatus.Failed, PrerequisiteStatus.Warning)]
    public async Task dependencies_are_declared_by_checks_even_when_registered_after_the_dependent(
        PrerequisiteStatus prerequisiteStatus, PrerequisiteStatus dependentStatus)
    {
        var prerequisite = new TaskCompletionSource<PrerequisiteCheck>(TaskCreationOptions.RunContinuationsAsynchronously);
        var validator = new PrerequisiteValidator(
        [
            new StubCheck("dependent", _ => Task.FromResult(new PrerequisiteCheck("dependent",
                prerequisite.Task.IsCompletedSuccessfully ? PrerequisiteStatus.Passed : PrerequisiteStatus.Failed, "Dependency completed.")), "prerequisite"),
            new StubCheck("prerequisite", _ => prerequisite.Task),
            StubCheck.Returning("independent", PrerequisiteStatus.Passed),
        ]);

        Task<PrerequisiteReport> pending = validator.ValidateAsync(CancellationToken.None);
        prerequisite.SetResult(new PrerequisiteCheck("prerequisite", prerequisiteStatus, "Dependency outcome."));
        PrerequisiteReport report = await pending;

        Assert.Equal(["dependent", "prerequisite", "independent"], report.Checks.Select(check => check.Name));
        Assert.Equal(dependentStatus, report.Checks[0].Status);
        Assert.Equal(PrerequisiteStatus.Passed, report.Checks[2].Status);
    }

    [Fact]
    public async Task a_warning_prerequisite_cannot_hide_a_failed_dependent_check_and_make_the_app_ready()
    {
        var validator = new PrerequisiteValidator(
        [
            StubCheck.Returning("prerequisite", PrerequisiteStatus.Warning),
            new StubCheck("dependent", _ => Task.FromResult(new PrerequisiteCheck(
                "dependent", PrerequisiteStatus.Failed, "Invalid configuration.")), "prerequisite"),
        ]);

        PrerequisiteReport report = await validator.ValidateAsync(CancellationToken.None);

        Assert.False(report.IsReady);
        Assert.Equal("Invalid configuration.", Assert.Single(report.Checks,
            check => check.Status == PrerequisiteStatus.Failed).Message);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task missing_or_cyclic_dependencies_are_reported_without_hiding_independent_checks(bool cyclic)
    {
        List<IPrerequisiteCheck> checks =
        [
            new StubCheck("dependent", _ => throw new InvalidOperationException("Must not run."), "missing"),
            StubCheck.Returning("independent", PrerequisiteStatus.Passed),
        ];
        if (cyclic)
        {
            checks.Add(new StubCheck("missing", _ => throw new InvalidOperationException("Must not run."), "dependent"));
        }

        PrerequisiteReport report = await new PrerequisiteValidator(checks).ValidateAsync(CancellationToken.None);

        PrerequisiteCheck dependent = Assert.Single(report.Checks, check => check.Name == "dependent");
        Assert.Equal(PrerequisiteStatus.Failed, dependent.Status);
        Assert.Contains("missing or cyclic", dependent.Message);
        Assert.Equal(PrerequisiteStatus.Passed, Assert.Single(report.Checks, check => check.Name == "independent").Status);
    }

    [Fact]
    public async Task a_throwing_check_becomes_a_failed_result_without_hiding_the_others()
    {
        var throwing = new StubCheck("explosive", _ => throw new InvalidOperationException("boom"));
        var validator = new PrerequisiteValidator([throwing, StubCheck.Returning("fine", PrerequisiteStatus.Passed)]);

        PrerequisiteReport report = await validator.ValidateAsync(CancellationToken.None);

        PrerequisiteCheck failed = Assert.Single(report.Checks, check => check.Name == "explosive");
        Assert.Equal(PrerequisiteStatus.Failed, failed.Status);
        Assert.Contains("boom", failed.Message);
        Assert.Equal(PrerequisiteStatus.Passed, Assert.Single(report.Checks, check => check.Name == "fine").Status);
        Assert.False(report.IsReady);
    }

    [Fact]
    public async Task caller_cancellation_is_not_swallowed()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        var cancelling = new StubCheck("slow", token => Task.FromCanceled<PrerequisiteCheck>(token));
        var validator = new PrerequisiteValidator([cancelling]);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => validator.ValidateAsync(cts.Token));
    }

    [Fact]
    public async Task warnings_keep_the_app_ready()
    {
        var validator = new PrerequisiteValidator([StubCheck.Returning("optional", PrerequisiteStatus.Warning)]);

        PrerequisiteReport report = await validator.ValidateAsync(CancellationToken.None);

        Assert.True(report.IsReady);
    }

    [Fact]
    public async Task readiness_is_diagnostic_only_until_prerequisites_were_evaluated()
    {
        var readiness = new DiagnosticReadiness(new PrerequisiteValidator([]), new FixedClock(Now));

        Assert.Equal(ReadinessMode.DiagnosticOnly, readiness.Current.Mode);
        Assert.Null(readiness.Current.Report);
        await readiness.RefreshAsync(CancellationToken.None);
        Assert.Equal(ReadinessMode.Operational, readiness.Current.Mode);
    }

    [Fact]
    public async Task failed_prerequisites_produce_diagnostic_only_status_with_the_failing_checks()
    {
        var validator = new PrerequisiteValidator(
        [
            StubCheck.Returning("ok", PrerequisiteStatus.Passed),
            StubCheck.Returning("broken", PrerequisiteStatus.Failed),
        ]);
        var readiness = new DiagnosticReadiness(validator, new FixedClock(Now));

        ReadinessSnapshot snapshot = await readiness.RefreshAsync(CancellationToken.None);

        Assert.Equal(ReadinessMode.DiagnosticOnly, snapshot.Mode);
        Assert.Same(snapshot, readiness.Current);
        Assert.Equal(Now, snapshot.EvaluatedAt);
        Assert.Equal("broken", Assert.Single(snapshot.FailedChecks).Name);
    }

    [Fact]
    public async Task readiness_recovers_to_operational_when_a_later_evaluation_passes()
    {
        var fixedIt = false;
        var flaky = new StubCheck("flaky", _ => Task.FromResult(new PrerequisiteCheck(
            "flaky", fixedIt ? PrerequisiteStatus.Passed : PrerequisiteStatus.Failed, "state")));
        var readiness = new DiagnosticReadiness(new PrerequisiteValidator([flaky]), new FixedClock(Now));

        await readiness.RefreshAsync(CancellationToken.None);
        Assert.Equal(ReadinessMode.DiagnosticOnly, readiness.Current.Mode);
        fixedIt = true;
        await readiness.RefreshAsync(CancellationToken.None);

        Assert.Equal(ReadinessMode.Operational, readiness.Current.Mode);
        Assert.Empty(readiness.Current.FailedChecks);
    }
}
