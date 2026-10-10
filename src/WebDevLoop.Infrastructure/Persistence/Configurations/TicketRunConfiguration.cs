using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WebDevLoop.Core.Domain;

namespace WebDevLoop.Infrastructure.Persistence.Configurations;

internal sealed class TicketRunConfiguration : IEntityTypeConfiguration<TicketRun>
{
    public void Configure(EntityTypeBuilder<TicketRun> builder)
    {
        builder.ToTable("TicketRuns");
        builder.HasKey(ticket => ticket.Id);
        builder.Property(ticket => ticket.Id).ValueGeneratedNever();
        builder.Property(ticket => ticket.Title).IsRequired();
        builder.Property(ticket => ticket.BodySnapshot).IsRequired();
        builder.ComplexProperty(ticket => ticket.Issue, IssueRefColumns.Configure);
        builder.Property(ticket => ticket.Attention).HasConversion<AttentionReasonConverter>();
        builder.HasOne<SpecRun>().WithMany().HasForeignKey(ticket => ticket.SpecRunId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(ticket => new { ticket.SpecRunId, ticket.Status }).HasDatabaseName("IX_TicketRuns_SpecRun_Status");
    }
}

internal sealed class TicketDependencyConfiguration : IEntityTypeConfiguration<TicketDependency>
{
    public void Configure(EntityTypeBuilder<TicketDependency> builder)
    {
        builder.ToTable("TicketDependencies");
        builder.HasKey(dependency => dependency.Id);
        builder.HasOne<SpecRun>().WithMany().HasForeignKey(dependency => dependency.SpecRunId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<TicketRun>().WithMany().HasForeignKey(dependency => dependency.BlockedTicketRunId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<TicketRun>().WithMany().HasForeignKey(dependency => dependency.BlockingTicketRunId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(dependency => new { dependency.BlockedTicketRunId, dependency.BlockingTicketRunId })
            .IsUnique()
            .HasDatabaseName("UX_TicketDependencies_Blocked_Blocking");
        builder.HasIndex(dependency => dependency.SpecRunId).HasDatabaseName("IX_TicketDependencies_SpecRun");
    }
}
