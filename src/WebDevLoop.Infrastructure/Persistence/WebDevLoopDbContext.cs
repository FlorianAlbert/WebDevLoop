using Microsoft.EntityFrameworkCore;
using WebDevLoop.Core.Domain;

namespace WebDevLoop.Infrastructure.Persistence;

public sealed class WebDevLoopDbContext(DbContextOptions<WebDevLoopDbContext> options) : DbContext(options)
{
    public DbSet<RepositoryRecord> Repositories => Set<RepositoryRecord>();

    public DbSet<SpecRun> SpecRuns => Set<SpecRun>();

    public DbSet<SpecDependency> SpecDependencies => Set<SpecDependency>();

    public DbSet<TicketRun> TicketRuns => Set<TicketRun>();

    public DbSet<TicketDependency> TicketDependencies => Set<TicketDependency>();

    public DbSet<StepRun> StepRuns => Set<StepRun>();

    public DbSet<IntegrationSaga> IntegrationSagas => Set<IntegrationSaga>();

    public DbSet<PullStackLayer> PullStackLayers => Set<PullStackLayer>();

    public DbSet<FindingIssuance> FindingIssuances => Set<FindingIssuance>();

    public DbSet<TestLease> TestLeases => Set<TestLease>();

    public DbSet<SettingsProfile> SettingsProfiles => Set<SettingsProfile>();

    public DbSet<RunEvent> RunEvents => Set<RunEvent>();

    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    public DbSet<AgentLogRecord> AgentLogEntries => Set<AgentLogRecord>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder) =>
        ValueConverterConventions.Apply(configurationBuilder);

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(WebDevLoopDbContext).Assembly);

        foreach (var entityType in modelBuilder.Model.GetEntityTypes().Where(type => type.ClrType.IsAssignableTo(typeof(VersionedEntity))))
        {
            modelBuilder.Entity(entityType.ClrType).Property(nameof(VersionedEntity.Version)).IsConcurrencyToken();
        }
    }
}
