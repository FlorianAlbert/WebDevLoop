using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WebDevLoop.Core.Domain;

namespace WebDevLoop.Infrastructure.Persistence.Configurations;

internal sealed class StepRunConfiguration : IEntityTypeConfiguration<StepRun>
{
    public void Configure(EntityTypeBuilder<StepRun> builder)
    {
        builder.ToTable("StepRuns");
        builder.HasKey(step => step.Id);
        builder.Property(step => step.Id).ValueGeneratedNever();
        builder.Property(step => step.InputPromptHash).IsRequired();
        builder.HasOne<SpecRun>().WithMany().HasForeignKey(step => step.SpecRunId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<TicketRun>().WithMany().HasForeignKey(step => step.TicketRunId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(step => step.SpecRunId).HasDatabaseName("IX_StepRuns_SpecRun");
        builder.HasIndex(step => step.TicketRunId).HasDatabaseName("IX_StepRuns_TicketRun");
        builder.HasIndex(step => step.Status).HasDatabaseName("IX_StepRuns_Status");

        builder.HasIndex(step => step.TicketRunId)
            .IsUnique()
            .HasFilter($"{FilteredIndexSql.ImplementOrFixStep} AND {FilteredIndexSql.ActiveStep}")
            .HasDatabaseName("UX_StepRuns_ActiveImplementOrFixPerTicket");

        builder.HasIndex(step => new { step.SpecRunId, step.Kind })
            .IsUnique()
            .HasFilter($"\"AgentRole\" IS NULL AND {FilteredIndexSql.ActiveStep}")
            .HasDatabaseName("UX_StepRuns_ActiveAppOwnedKindPerRun");
    }
}
