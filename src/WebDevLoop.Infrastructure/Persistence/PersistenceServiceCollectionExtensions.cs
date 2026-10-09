using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using WebDevLoop.Core.Ports;
using WebDevLoop.Infrastructure.Persistence.Repositories;

namespace WebDevLoop.Infrastructure.Persistence;

public static class PersistenceServiceCollectionExtensions
{
    /// <summary>Registers the SQLite context and every persistence port. One context (and so one unit of work) per scope.</summary>
    public static IServiceCollection AddPersistence(this IServiceCollection services, string connectionString)
    {
        services.AddDbContext<WebDevLoopDbContext>(options => options.UseSqlite(connectionString));
        services.AddScoped<IUnitOfWork, EfUnitOfWork>();
        services.AddScoped<IRepositoryRecordRepository, EfRepositoryRecordRepository>();
        services.AddScoped<ISpecRunRepository, EfSpecRunRepository>();
        services.AddScoped<ITicketRunRepository, EfTicketRunRepository>();
        services.AddScoped<IStepRunRepository, EfStepRunRepository>();
        services.AddScoped<IIntegrationSagaRepository, EfIntegrationSagaRepository>();
        services.AddScoped<IPullStackLayerRepository, EfPullStackLayerRepository>();
        services.AddScoped<IFindingIssuanceRepository, EfFindingIssuanceRepository>();
        services.AddScoped<ITestLeaseRepository, EfTestLeaseRepository>();
        services.AddScoped<ISettingsProfileRepository, EfSettingsProfileRepository>();
        services.AddScoped<IRunEventRepository, EfRunEventRepository>();
        services.AddScoped<IOutboxMessageRepository, EfOutboxMessageRepository>();

        return services;
    }
}
