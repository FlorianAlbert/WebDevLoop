using System.Diagnostics;
using System.Globalization;
using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Ports;
using WebDevLoop.Infrastructure.TestHost;

namespace WebDevLoop.Infrastructure.Tests.TestHost;

/// <summary>Real processes on Linux: a detached leftover started with a lease marker is found through /proc and killed.</summary>
public sealed class ProcessCleanupTests
{
    private static readonly RunId Run = new($"cleanup-{Guid.NewGuid():N}");

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Process_table_reads_parent_group_and_lease_marker()
    {
        Assert.SkipUnless(OperatingSystem.IsLinux(), "The process table reads /proc.");
        string marker = TestTargetEnvironment.LeaseMarker(Run, 41000);
        int leftover = await StartDetachedSleepAsync(marker);
        try
        {
            var table = new ProcFileSystemProcessTable();

            IReadOnlyList<HostProcess> snapshot = table.Snapshot();

            HostProcess process = Assert.Single(snapshot, candidate => candidate.Id == leftover);
            Assert.Equal(marker, process.LeaseMarker);
            Assert.NotEqual(table.CurrentProcessId, process.ParentId);
            HostProcess self = Assert.Single(snapshot, candidate => candidate.Id == table.CurrentProcessId);
            Assert.Null(self.LeaseMarker);
            Assert.True(self.ProcessGroupId > 0);
        }
        finally
        {
            new ProcessTerminator().Kill(leftover);
        }
    }

    [Fact]
    public async Task Stopping_a_target_kills_a_detached_leftover_the_tester_started()
    {
        Assert.SkipUnless(OperatingSystem.IsLinux(), "Leftover processes are found through /proc.");
        string marker = TestTargetEnvironment.LeaseMarker(Run, 41001);
        int leftover = await StartDetachedSleepAsync(marker);
        var runner = new TestTargetRunner(new LoopbackPortProbe(new TestHostOptions()), new ProcFileSystemProcessTable(), new ProcessTerminator(), new TestHostOptions());

        TestTargetStopResult result = await runner.StopAsync(
            new TestTarget(Run, 41001, TestTargetEnvironment.AppUrl(41001), new Dictionary<string, string>()), Token);

        Assert.Equal(1, result.TerminatedProcessCount);
        Assert.True(await ExitsAsync(leftover), $"Process {leftover} is still running.");
    }

    /// <summary>Starts <c>sleep</c> in the background of a shell that exits at once, so the sleep is no child of this process.</summary>
    private static async Task<int> StartDetachedSleepAsync(string marker)
    {
        var start = new ProcessStartInfo("/bin/sh", ["-c", "sleep 60 >/dev/null 2>&1 & echo $!"])
        {
            RedirectStandardOutput = true,
            UseShellExecute = false,
        };
        start.Environment[TestTargetEnvironment.LeaseVariable] = marker;
        using Process shell = Process.Start(start)!;
        string pid = await shell.StandardOutput.ReadLineAsync(Token) ?? throw new InvalidOperationException("The shell printed no pid.");
        await shell.WaitForExitAsync(Token);
        return int.Parse(pid.Trim(), CultureInfo.InvariantCulture);
    }

    private static async Task<bool> ExitsAsync(int processId)
    {
        for (int poll = 0; poll < 50; poll++)
        {
            if (!IsAlive(processId))
            {
                return true;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(100), Token);
        }

        return false;
    }

    /// <summary>A killed process may linger as a zombie until its new parent reaps it; that counts as gone.</summary>
    private static bool IsAlive(int processId)
    {
        string stat = $"/proc/{processId}/stat";
        if (!File.Exists(stat))
        {
            return false;
        }

        try
        {
            string content = File.ReadAllText(stat);
            return content[(content.LastIndexOf(')') + 2)..][0] != 'Z';
        }
        catch (IOException)
        {
            return false;
        }
    }
}
