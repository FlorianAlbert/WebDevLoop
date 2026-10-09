using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using WebDevLoop.Infrastructure.Persistence;
using WebDevLoop.Infrastructure.Queries;

namespace WebDevLoop.Infrastructure.Tests.Queries;

/// <summary>A migrated SQLite file that stores built on it share, so a second store on the same file models an app restart.</summary>
public sealed class AgentLogDatabase : IDisposable
{
    private readonly string _databasePath = Path.Combine(AppContext.BaseDirectory, "test-databases", $"{Guid.NewGuid():N}.db");
    private readonly ServiceProvider _provider;

    public AgentLogDatabase()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_databasePath)!);
        _provider = new ServiceCollection().AddPersistence($"Data Source={_databasePath}").BuildServiceProvider();

        using IServiceScope scope = _provider.CreateScope();
        PersistenceDatabase.MigrateAsync(scope.ServiceProvider.GetRequiredService<WebDevLoopDbContext>(), CancellationToken.None).GetAwaiter().GetResult();
    }

    public PersistentAgentLogStore CreateStore(AgentLogStoreOptions? options = null) =>
        new(options ?? new AgentLogStoreOptions(), _provider.GetRequiredService<IServiceScopeFactory>());

    public async Task<int> CountStoredEntriesAsync()
    {
        using IServiceScope scope = _provider.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<WebDevLoopDbContext>().AgentLogEntries.CountAsync();
    }

    public void Dispose()
    {
        _provider.Dispose();
        SqliteConnection.ClearAllPools();
        foreach (string file in Directory.GetFiles(Path.GetDirectoryName(_databasePath)!, Path.GetFileName(_databasePath) + "*"))
        {
            File.Delete(file);
        }
    }
}
