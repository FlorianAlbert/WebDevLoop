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
