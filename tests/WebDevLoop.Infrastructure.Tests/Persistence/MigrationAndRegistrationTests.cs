using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Ports;
using WebDevLoop.Infrastructure.Persistence;
using WebDevLoop.Infrastructure.Persistence.Repositories;

namespace WebDevLoop.Infrastructure.Tests.Persistence;

public sealed class MigrationAndRegistrationTests : IDisposable
{
    private readonly string _databasePath = Path.Combine(AppContext.BaseDirectory, "test-databases", $"{Guid.NewGuid():N}.db");

    public MigrationAndRegistrationTests() => Directory.CreateDirectory(Path.GetDirectoryName(_databasePath)!);

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        foreach (string file in Directory.GetFiles(Path.GetDirectoryName(_databasePath)!, Path.GetFileName(_databasePath) + "*"))
        {
            File.Delete(file);
        }
    }

    [Fact]
    public async Task migrations_fully_describe_the_current_model()
    {
        using ServiceProvider provider = BuildProvider();
        using IServiceScope scope = provider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<WebDevLoopDbContext>();

        await PersistenceDatabase.MigrateAsync(context, CancellationToken.None);

        Assert.Empty(await context.Database.GetPendingMigrationsAsync(TestContext.Current.CancellationToken));
        Assert.NotEmpty(await context.Database.GetAppliedMigrationsAsync(TestContext.Current.CancellationToken));
        Assert.False(context.Database.HasPendingModelChanges(), "Model changed without a migration; add one with dotnet ef migrations add.");
    }

    [Fact]
    public async Task migrating_twice_is_harmless_and_uses_write_ahead_logging_for_concurrent_workers()
    {
        using ServiceProvider provider = BuildProvider();
        using IServiceScope scope = provider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<WebDevLoopDbContext>();

        await PersistenceDatabase.MigrateAsync(context, CancellationToken.None);
        await PersistenceDatabase.MigrateAsync(context, CancellationToken.None);

        var connection = context.Database.GetDbConnection();
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA journal_mode";
        Assert.Equal("wal", (string?)await command.ExecuteScalarAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task upgrading_a_database_dates_existing_spec_runs_status_change_to_their_latest_known_transition()
    {
        const string BeforeStatusChangedAt = "20261009153925_RunNeedsAttentionPhase";
        DateTimeOffset ready = TestData.Now.AddHours(2);
        using ServiceProvider provider = BuildProvider();
        using (IServiceScope seed = provider.CreateScope())
        {
            var context = seed.ServiceProvider.GetRequiredService<WebDevLoopDbContext>();
            await PersistenceDatabase.MigrateAsync(context, CancellationToken.None);
            RepositoryRecord repository = TestData.NewRepository();
            context.Add(repository);
            await context.SaveChangesAsync(TestContext.Current.CancellationToken);
            SpecRun queued = TestData.NewSpecRun(repository.Id, "run-queued", issueNumber: 10, queuePosition: 1);
            SpecRun reviewable = TestData.NewSpecRun(repository.Id, "run-ready", issueNumber: 11, queuePosition: 2);
            reviewable.TransitionTo(SpecRunStatus.Preparing, TestData.Now);
            foreach (SpecRunStatus next in new[] { SpecRunStatus.Running, SpecRunStatus.ParentReviewing, SpecRunStatus.Testing })
            {
                reviewable.TransitionTo(next, TestData.Now.AddHours(1));
            }

            reviewable.TransitionTo(SpecRunStatus.ReadyForReview, ready);
            context.AddRange(queued, reviewable);
            await context.SaveChangesAsync(TestContext.Current.CancellationToken);
            IMigrator migrator = context.GetService<IMigrator>();
            await migrator.MigrateAsync(BeforeStatusChangedAt, TestContext.Current.CancellationToken);
            await migrator.MigrateAsync(cancellationToken: TestContext.Current.CancellationToken);
        }

        using IServiceScope read = provider.CreateScope();
        ISpecRunRepository specRuns = read.ServiceProvider.GetRequiredService<ISpecRunRepository>();
        Assert.Equal(TestData.Now, (await specRuns.GetAsync(new RunId("run-queued"), CancellationToken.None))!.StatusChangedAt);
        Assert.Equal(ready, (await specRuns.GetAsync(new RunId("run-ready"), CancellationToken.None))!.StatusChangedAt);
    }

    [Fact]
    public async Task every_persistence_port_resolves_per_scope_and_shares_one_unit_of_work()
    {
        using ServiceProvider provider = BuildProvider();
        using (IServiceScope migrate = provider.CreateScope())
        {
            await PersistenceDatabase.MigrateAsync(migrate.ServiceProvider.GetRequiredService<WebDevLoopDbContext>(), CancellationToken.None);
        }

        using (IServiceScope write = provider.CreateScope())
        {
            IServiceProvider services = write.ServiceProvider;
            Type[] ports =
            [
                typeof(IRepositoryRecordRepository), typeof(ISpecRunRepository), typeof(ITicketRunRepository), typeof(IStepRunRepository),
                typeof(IIntegrationSagaRepository), typeof(IPullStackLayerRepository), typeof(IFindingIssuanceRepository), typeof(ITestLeaseRepository),
                typeof(ISettingsProfileRepository), typeof(IRunEventRepository), typeof(IOutboxMessageRepository), typeof(IUnitOfWork),
            ];
            Assert.All(ports, port => Assert.NotNull(services.GetRequiredService(port)));

            services.GetRequiredService<IRepositoryRecordRepository>().Add(TestData.NewRepository());
            services.GetRequiredService<IOutboxMessageRepository>().Add(OutboxMessage.Create("Registered", "{}", TestData.Now));
            Assert.Equal(SaveOutcome.Saved, await services.GetRequiredService<IUnitOfWork>().SaveChangesAsync(CancellationToken.None));
        }

        using IServiceScope read = provider.CreateScope();
        Assert.Single(await read.ServiceProvider.GetRequiredService<IRepositoryRecordRepository>().ListAsync(CancellationToken.None));
        Assert.Single(await read.ServiceProvider.GetRequiredService<IOutboxMessageRepository>().ListPendingAsync(10, CancellationToken.None));
    }

    private ServiceProvider BuildProvider()
    {
        var services = new ServiceCollection();
        services.AddPersistence($"Data Source={_databasePath}");
        return services.BuildServiceProvider(validateScopes: true);
    }
}
