using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WebDevLoop.Core.Domain;

namespace WebDevLoop.Infrastructure.Persistence.Configurations;

internal sealed class FindingIssuanceConfiguration : IEntityTypeConfiguration<FindingIssuance>
{
    public void Configure(EntityTypeBuilder<FindingIssuance> builder)
    {
        builder.ToTable("FindingIssuances");
        builder.HasKey(finding => finding.Id);
        builder.HasOne<SpecRun>().WithMany().HasForeignKey(finding => finding.SpecRunId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<StepRun>().WithMany().HasForeignKey(finding => finding.SourceStepRunId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(finding => new { finding.SpecRunId, finding.Fingerprint })
            .IsUnique()
            .HasDatabaseName("UX_FindingIssuances_SpecRun_Fingerprint");
        builder.HasIndex(finding => finding.SourceStepRunId).HasDatabaseName("IX_FindingIssuances_SourceStep");
    }
}

internal sealed class TestLeaseConfiguration : IEntityTypeConfiguration<TestLease>
{
    public void Configure(EntityTypeBuilder<TestLease> builder)
    {
        builder.ToTable("TestLeases");
        builder.HasKey(lease => lease.Id);
        builder.Property(lease => lease.WorkspacePath).IsRequired();
        builder.HasOne<SpecRun>().WithMany().HasForeignKey(lease => lease.SpecRunId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(lease => lease.SpecRunId)
            .IsUnique()
            .HasFilter(FilteredIndexSql.UnreleasedLease)
            .HasDatabaseName("UX_TestLeases_ActivePerRun");
        builder.HasIndex(lease => lease.Port)
            .IsUnique()
            .HasFilter(FilteredIndexSql.UnreleasedLease)
            .HasDatabaseName("UX_TestLeases_ActivePort");
    }
}

internal sealed class RunEventConfiguration : IEntityTypeConfiguration<RunEvent>
{
    public void Configure(EntityTypeBuilder<RunEvent> builder)
    {
        builder.ToTable("RunEvents");
        builder.HasKey(runEvent => runEvent.Id);
        builder.Property(runEvent => runEvent.Type).IsRequired();
        builder.Property(runEvent => runEvent.PayloadJson).IsRequired();
        builder.HasOne<SpecRun>().WithMany().HasForeignKey(runEvent => runEvent.SpecRunId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<TicketRun>().WithMany().HasForeignKey(runEvent => runEvent.TicketRunId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(runEvent => runEvent.SpecRunId).HasDatabaseName("IX_RunEvents_SpecRun");
    }
}

internal sealed class OutboxMessageConfiguration : IEntityTypeConfiguration<OutboxMessage>
{
    public void Configure(EntityTypeBuilder<OutboxMessage> builder)
    {
        builder.ToTable("OutboxMessages");
        builder.HasKey(message => message.Id);
        builder.Property(message => message.Type).IsRequired();
        builder.Property(message => message.PayloadJson).IsRequired();
        builder.HasIndex(message => message.DispatchedAt)
            .HasFilter("\"DispatchedAt\" IS NULL")
            .HasDatabaseName("IX_OutboxMessages_Pending");
    }
}
