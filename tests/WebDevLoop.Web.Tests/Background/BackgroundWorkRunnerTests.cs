using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using WebDevLoop.Web.Background;

namespace WebDevLoop.Web.Tests.Background;

public sealed class BackgroundWorkRunnerTests : IAsyncDisposable
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);
    private readonly ServiceProvider _services;
    private readonly BackgroundWorkRunner _runner;

    public BackgroundWorkRunnerTests()
    {
        _services = new ServiceCollection().AddScoped<ScopedProbe>().BuildServiceProvider(validateScopes: true);
        _runner = new BackgroundWorkRunner(_services.GetRequiredService<IServiceScopeFactory>(), NullLogger<BackgroundWorkRunner>.Instance);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask DisposeAsync()
    {
        await _runner.DisposeAsync();
        await _services.DisposeAsync();
    }

    [Fact]
    public async Task each_launch_returns_immediately_and_runs_in_a_scope_of_its_own()
    {
        var release = new TaskCompletionSource();
        var probes = new List<ScopedProbe>();
        var finished = new CountdownEvent(2);

        foreach (string key in new[] { "a", "b" })
        {
            _runner.Run(key, async (services, _) =>
            {
                ScopedProbe probe = services.GetRequiredService<ScopedProbe>();
                lock (probes)
                {
                    probes.Add(probe);
                }

                await release.Task;
                finished.Signal();
            });
        }

        Assert.Equal(2, _runner.RunningCount);
        release.SetResult();
        Assert.True(finished.Wait(Timeout, Ct));
        Assert.Equal(2, probes.Distinct().Count());
        await WaitUntilAsync(() => _runner.RunningCount == 0);
        Assert.All(probes, probe => Assert.True(probe.Disposed));
    }

    [Fact]
    public async Task a_launch_while_the_same_key_runs_reruns_once_afterwards_and_never_concurrently()
    {
        var release = new TaskCompletionSource();
        int runs = 0;
        int concurrent = 0;
        int peak = 0;
        Func<IServiceProvider, CancellationToken, Task> work = async (_, _) =>
        {
            peak = Math.Max(peak, Interlocked.Increment(ref concurrent));
            Interlocked.Increment(ref runs);
            await release.Task;
            Interlocked.Decrement(ref concurrent);
        };

        _runner.Run("ticket", work);
        _runner.Run("ticket", work);
        _runner.Run("ticket", work);
        release.SetResult();

        await WaitUntilAsync(() => _runner.RunningCount == 0);
        Assert.Equal(2, runs);
        Assert.Equal(1, peak);
    }

    [Fact]
    public async Task stopping_cancels_running_work_waits_for_it_and_ignores_later_launches()
    {
        var started = new TaskCompletionSource();
        bool wound_down = false;
        _runner.Run("hanging", async (_, cancellationToken) =>
        {
            started.SetResult();
            try
            {
                await Task.Delay(System.Threading.Timeout.InfiniteTimeSpan, cancellationToken);
            }
            finally
            {
                await Task.Delay(50, CancellationToken.None);
                wound_down = true;
            }
        });
        await started.Task.WaitAsync(Timeout, Ct);

        await _runner.StopAsync(Ct);
        bool launchedAfterStop = false;
        _runner.Run("late", (_, _) => { launchedAfterStop = true; return Task.CompletedTask; });

        Assert.True(wound_down);
        Assert.Equal(0, _runner.RunningCount);
        await Task.Delay(100, Ct);
        Assert.False(launchedAfterStop);
    }

    [Fact]
    public async Task a_failing_item_does_not_stop_later_launches_of_its_key()
    {
        _runner.Run("flaky", (_, _) => throw new InvalidOperationException("boom"));
        await WaitUntilAsync(() => _runner.RunningCount == 0);
        var succeeded = new TaskCompletionSource();

        _runner.Run("flaky", (_, _) => { succeeded.SetResult(); return Task.CompletedTask; });

        await succeeded.Task.WaitAsync(Timeout, Ct);
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        deadline.CancelAfter(Timeout);
        while (!condition())
        {
            await Task.Delay(10, deadline.Token);
        }
    }

    private sealed class ScopedProbe : IDisposable
    {
        public bool Disposed { get; private set; }

        public void Dispose() => Disposed = true;
    }
}
