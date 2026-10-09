using Microsoft.EntityFrameworkCore;
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
