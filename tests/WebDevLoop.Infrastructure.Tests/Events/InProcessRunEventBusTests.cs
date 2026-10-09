using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Events;
using WebDevLoop.Infrastructure.Events;

namespace WebDevLoop.Infrastructure.Tests.Events;

public sealed class InProcessRunEventBusTests
{
    private static readonly EventEnvelope Sample =
        new(1, new FrontierReconciliationRequested(new RunId("run-1"), new DateTimeOffset(2026, 10, 9, 12, 0, 0, TimeSpan.Zero)));

    [Fact]
    public async Task Every_subscriber_receives_the_published_envelope()
    {
        var bus = new InProcessRunEventBus(NullLogger<InProcessRunEventBus>.Instance);
        List<string> received = [];
        bus.Subscribe((_, _) => { received.Add("a"); return Task.CompletedTask; });
        bus.Subscribe((_, _) => { received.Add("b"); return Task.CompletedTask; });

        await bus.PublishAsync(Sample, CancellationToken.None);

        Assert.Equal(["a", "b"], received);
    }

    [Fact]
    public async Task A_throwing_subscriber_does_not_block_the_others_and_is_logged()
    {
        var logger = new CapturingLogger<InProcessRunEventBus>();
        var bus = new InProcessRunEventBus(logger);
        List<string> received = [];
        bus.Subscribe((_, _) => throw new InvalidOperationException("sync boom"));
        bus.Subscribe((_, _) => Task.FromException(new InvalidOperationException("async boom")));
        bus.Subscribe((_, _) => { received.Add("healthy"); return Task.CompletedTask; });

        await bus.PublishAsync(Sample, CancellationToken.None);

        Assert.Equal(["healthy"], received);
        Assert.Equal(2, logger.ErrorCount);
    }

    [Fact]
    public async Task Disposing_the_subscription_stops_delivery()
    {
        var bus = new InProcessRunEventBus(NullLogger<InProcessRunEventBus>.Instance);
        int calls = 0;
        IDisposable subscription = bus.Subscribe((_, _) => { calls++; return Task.CompletedTask; });

        await bus.PublishAsync(Sample, CancellationToken.None);
        subscription.Dispose();
        await bus.PublishAsync(Sample, CancellationToken.None);

        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task Cancellation_is_propagated_instead_of_swallowed()
    {
        var bus = new InProcessRunEventBus(NullLogger<InProcessRunEventBus>.Instance);
        using var cts = new CancellationTokenSource();
        bus.Subscribe((_, token) => Task.FromCanceled(token));
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => bus.PublishAsync(Sample, cts.Token));
    }
}

internal sealed class CapturingLogger<T> : ILogger<T>
{
    public int ErrorCount { get; private set; }

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        if (logLevel >= LogLevel.Error)
        {
            ErrorCount++;
        }
    }
}
