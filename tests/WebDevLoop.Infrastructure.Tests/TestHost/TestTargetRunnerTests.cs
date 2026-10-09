using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Ports;
using WebDevLoop.Infrastructure.TestHost;

namespace WebDevLoop.Infrastructure.Tests.TestHost;

public sealed class TestTargetRunnerTests
{
    private const int AppProcess = 400;

    private static readonly RunId Run = new("run-1");
    private static readonly RunId OtherRun = new("run-2");

    private readonly FakePortProbe _ports = new();
    private readonly RecordingTerminator _terminator = new();
    private readonly TestHostOptions _options = new() { ReadinessPollInterval = TimeSpan.FromMilliseconds(5) };

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Reserved_port_is_free_and_passed_to_the_tester_as_url_and_environment()
    {
        _ports.Busy.Add(41000);

        TestTarget target = (await Runner().ReserveAsync(Run, new TestPortRange(41000, 41002), Token))!;

        Assert.Equal((Run, 41001), (target.SpecRunId, target.Port));
        Assert.Equal(new Uri("http://localhost:41001/"), target.AppUrl);
        Assert.Equal("41001", target.Environment[TestTargetEnvironment.PortVariable]);
        Assert.Equal("41001", target.Environment[TestTargetEnvironment.ReservedPortVariable]);
        Assert.Equal("http://localhost:41001/", target.Environment[TestTargetEnvironment.AppUrlVariable]);
        Assert.Equal("run-1:41001", target.Environment[TestTargetEnvironment.LeaseVariable]);
    }

    [Fact]
    public async Task Reserved_ports_are_isolated_per_run_until_stopped()
    {
        TestTargetRunner runner = Runner();
        var range = new TestPortRange(41000, 41001);

        TestTarget first = (await runner.ReserveAsync(Run, range, Token))!;
        TestTarget second = (await runner.ReserveAsync(OtherRun, range, Token))!;
        TestTarget? none = await runner.ReserveAsync(new RunId("run-3"), range, Token);
        await runner.StopAsync(first, Token);
        TestTarget? reused = await runner.ReserveAsync(new RunId("run-3"), range, Token);

        Assert.Equal([41000, 41001], [first.Port, second.Port]);
        Assert.Null(none);
        Assert.Equal(41000, reused!.Port);
    }

    [Fact]
    public async Task Readiness_waits_until_the_application_accepts_connections()
    {
        _ports.ProbesUntilAccepting = 3;
        TestTargetRunner runner = Runner();
        TestTarget target = (await runner.ReserveAsync(Run, new TestPortRange(41000, 41000), Token))!;

        TestTargetReadiness readiness = await runner.WaitForReadyAsync(target, TimeSpan.FromSeconds(10), Token);

        Assert.Equal(TestTargetReadiness.Ready, readiness);
        Assert.Equal(4, _ports.Probes);
    }

    [Fact]
    public async Task Readiness_times_out_when_nothing_ever_listens()
    {
        TestTargetRunner runner = Runner();
        TestTarget target = (await runner.ReserveAsync(Run, new TestPortRange(41000, 41000), Token))!;

        TestTargetReadiness readiness = await runner.WaitForReadyAsync(target, TimeSpan.FromMilliseconds(60), Token);

        Assert.Equal(TestTargetReadiness.TimedOut, readiness);
        Assert.True(_ports.Probes > 1);
    }

    [Fact]
    public async Task Stop_kills_the_leftover_process_group_of_the_lease_but_nothing_else()
    {
        const int Self = 100;
        string marker = TestTargetEnvironment.LeaseMarker(Run, 41000);
        var table = new FakeProcessTable(
            Self,
            new HostProcess(Self, 1, Self, null),
            new HostProcess(200, Self, Self, marker),
            new HostProcess(300, 200, Self, marker),
            new HostProcess(AppProcess, 1, AppProcess, marker),
            new HostProcess(401, AppProcess, AppProcess, null),
            new HostProcess(402, AppProcess, AppProcess, marker),
            new HostProcess(500, 1, 500, TestTargetEnvironment.LeaseMarker(OtherRun, 41001)),
            new HostProcess(600, Self, Self, null));
        _terminator.Gone.Add(402);
        TestTargetRunner runner = Runner(table);
        TestTarget target = (await runner.ReserveAsync(Run, new TestPortRange(41000, 41000), Token))!;

        TestTargetStopResult result = await runner.StopAsync(target, Token);

        Assert.Equal([300, AppProcess, 401], _terminator.Killed.Order());
        Assert.Equal(3, result.TerminatedProcessCount);
    }

    [Fact]
    public async Task Stop_after_an_app_restart_finds_leftovers_by_their_lease_marker()
    {
        string marker = TestTargetEnvironment.LeaseMarker(Run, 41000);
        var table = new FakeProcessTable(100, new HostProcess(AppProcess, 1, 350, marker));

        TestTargetStopResult result = await Runner(table).StopAsync(
            new TestTarget(Run, 41000, TestTargetEnvironment.AppUrl(41000), new Dictionary<string, string>()), Token);

        Assert.Equal([AppProcess], _terminator.Killed);
        Assert.Equal(1, result.TerminatedProcessCount);
    }

    private TestTargetRunner Runner(IProcessTable? table = null) =>
        new(_ports, table ?? new FakeProcessTable(100), _terminator, _options);
}
