using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using WebDevLoop.Core.Events;
using WebDevLoop.Core.Management;
using WebDevLoop.Core.Orchestration.Control;
using WebDevLoop.Core.Ports;
using WebDevLoop.Core.Queries;
using WebDevLoop.Infrastructure.Events;
using WebDevLoop.Infrastructure.Prerequisites;
using WebDevLoop.Web.DependencyInjection;

namespace WebDevLoop.Web.Tests.Api;

/// <summary>Hosts the real web app (Program.cs) with every application service replaced by a fake.</summary>
internal sealed class ApiFactory : WebApplicationFactory<WebAssemblyMarker>
{
    public FakeRepositoryRegistry Registry { get; } = new();

    public FakeRepositoryQueries Repositories { get; } = new();

    public FakeSettingsManager Settings { get; } = new();

    public FakeSpecEnqueuer Enqueuer { get; } = new();

    public FakeRunQueries Runs { get; } = new();

    public FakeRunControl Control { get; } = new();

    public FakeAgentLogReader Logs { get; } = new();

    public CurrentRepositorySelection Selection { get; } = new();

    public CountingEventBus EventBus { get; } = new();

    public FakePrerequisiteValidator Validator { get; } = new();

    public DiagnosticReadiness Readiness { get; }

    public ApiFactory() => Readiness = new DiagnosticReadiness(Validator, new FakeClock());

    /// <summary>Operational by default; pass failing checks to enter diagnostic-only mode.</summary>
    public async Task<ApiFactory> EvaluateAsync(params PrerequisiteCheck[] checks)
    {
        if (checks.Length > 0)
        {
            Validator.Checks = checks;
        }

        await Readiness.RefreshAsync(CancellationToken.None);
        return this;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder) =>
        builder.ConfigureTestServices(services =>
        {
            services.AddWebDevLoopApi();
            services.AddSingleton<IRepositoryRegistry>(Registry);
            services.AddSingleton<IRepositoryQueries>(Repositories);
            services.AddSingleton<ISettingsManager>(Settings);
            services.AddSingleton<ISpecEnqueuer>(Enqueuer);
            services.AddSingleton<IRunQueries>(Runs);
            services.AddSingleton<IRunControl>(Control);
            services.AddSingleton<IAgentLogReader>(Logs);
            services.AddSingleton<ICurrentRepositorySelection>(Selection);
            services.AddSingleton<IRunEventBus>(EventBus);
            services.AddSingleton(Readiness);
        });
}

/// <summary>The real in-process bus plus a live subscriber count, so tests can see that streams unsubscribe.</summary>
internal sealed class CountingEventBus : IRunEventBus
{
    private readonly InProcessRunEventBus _inner = new(NullLogger<InProcessRunEventBus>.Instance);
    private int _subscribers;

    public int SubscriberCount => Volatile.Read(ref _subscribers);

    public Task PublishAsync(EventEnvelope envelope, CancellationToken cancellationToken) => _inner.PublishAsync(envelope, cancellationToken);

    public IDisposable Subscribe(Func<EventEnvelope, CancellationToken, Task> handler)
    {
        Interlocked.Increment(ref _subscribers);
        IDisposable subscription = _inner.Subscribe(handler);
        return new Unsubscriber(() =>
        {
            subscription.Dispose();
            Interlocked.Decrement(ref _subscribers);
        });
    }

    private sealed class Unsubscriber(Action dispose) : IDisposable
    {
        public void Dispose() => dispose();
    }
}
