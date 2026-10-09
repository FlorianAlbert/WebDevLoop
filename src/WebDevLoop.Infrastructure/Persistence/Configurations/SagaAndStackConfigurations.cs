using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WebDevLoop.Core.Domain;

namespace WebDevLoop.Infrastructure.Persistence.Configurations;

internal sealed class IntegrationSagaConfiguration : IEntityTypeConfiguration<IntegrationSaga>
{
    public void Configure(EntityTypeBuilder<IntegrationSaga> builder)
    {
        builder.ToTable("IntegrationSagas");
        builder.HasKey(saga => saga.Id);
        builder.Property(saga => saga.ExternalIdempotencyKey).IsRequired();
        builder.HasOne<SpecRun>().WithMany().HasForeignKey(saga => saga.SpecRunId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<TicketRun>().WithMany().HasForeignKey(saga => saga.TicketRunId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(saga => saga.TicketRunId).HasDatabaseName("IX_IntegrationSagas_TicketRun");
        builder.HasIndex(saga => saga.TicketRunId)
            .IsUnique()
            .HasFilter(FilteredIndexSql.IncompleteSaga)
            .HasDatabaseName("UX_IntegrationSagas_IncompletePerTicket");
    }
}

internal sealed class PullStackLayerConfiguration : IEntityTypeConfiguration<PullStackLayer>
{
    public void Configure(EntityTypeBuilder<PullStackLayer> builder)
    {
        builder.ToTable("PullStackLayers");
        builder.HasKey(layer => layer.Id);
        builder.HasOne<SpecRun>().WithMany().HasForeignKey(layer => layer.SpecRunId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<TicketRun>().WithMany().HasForeignKey(layer => layer.TicketRunId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(layer => new { layer.SpecRunId, layer.Position }).IsUnique().HasDatabaseName("UX_PullStackLayers_SpecRun_Position");
    }
}
