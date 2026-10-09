using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Ports;
using WebDevLoop.Infrastructure.Persistence;
using WebDevLoop.Infrastructure.Persistence.Repositories;

namespace WebDevLoop.Infrastructure.Tests.Persistence;

/// <summary>An isolated SQLite file database; every <see cref="OpenScope"/> is an independent context like a separate DI scope.</summary>
public sealed class PersistenceHarness : IDisposable
{
    private readonly string _databasePath = Path.Combine(AppContext.BaseDirectory, "test-databases", $"{Guid.NewGuid():N}.db");

    public PersistenceHarness()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_databasePath)!);

        using WebDevLoopDbContext context = CreateContext();
        PersistenceDatabase.MigrateAsync(context, CancellationToken.None).GetAwaiter().GetResult();
    }

    public PersistenceScope OpenScope() => new(CreateContext());

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        foreach (string file in Directory.GetFiles(Path.GetDirectoryName(_databasePath)!, Path.GetFileName(_databasePath) + "*"))
        {
            File.Delete(file);
        }
    }

    private WebDevLoopDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<WebDevLoopDbContext>()
            .UseSqlite($"Data Source={_databasePath}")
            .Options;

        return new WebDevLoopDbContext(options);
    }
}

public sealed class PersistenceScope(WebDevLoopDbContext context) : IDisposable
{
    public WebDevLoopDbContext Context { get; } = context;

    public IUnitOfWork UnitOfWork { get; } = new EfUnitOfWork(context);

    public IRepositoryRecordRepository Repositories { get; } = new EfRepositoryRecordRepository(context);

    public ISpecRunRepository SpecRuns { get; } = new EfSpecRunRepository(context);

    public ITicketRunRepository Tickets { get; } = new EfTicketRunRepository(context);

    public IStepRunRepository Steps { get; } = new EfStepRunRepository(context);

    public IIntegrationSagaRepository Sagas { get; } = new EfIntegrationSagaRepository(context);

    public IPullStackLayerRepository Layers { get; } = new EfPullStackLayerRepository(context);

    public IFindingIssuanceRepository Findings { get; } = new EfFindingIssuanceRepository(context);

    public ITestLeaseRepository Leases { get; } = new EfTestLeaseRepository(context);

    public ISettingsProfileRepository Settings { get; } = new EfSettingsProfileRepository(context);

    public IRunEventRepository Events { get; } = new EfRunEventRepository(context);

    public IOutboxMessageRepository Outbox { get; } = new EfOutboxMessageRepository(context);

    public async Task SaveAsync()
    {
        SaveOutcome outcome = await UnitOfWork.SaveChangesAsync(CancellationToken.None);
        Assert.Equal(SaveOutcome.Saved, outcome);
    }

    public void Dispose() => Context.Dispose();
}
