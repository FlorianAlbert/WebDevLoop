using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WebDevLoop.Core.Domain;

namespace WebDevLoop.Infrastructure.Persistence.Configurations;

internal sealed class SpecRunConfiguration : IEntityTypeConfiguration<SpecRun>
{
    public void Configure(EntityTypeBuilder<SpecRun> builder)
    {
        builder.ToTable("SpecRuns");
        builder.HasKey(run => run.Id);
        builder.Property(run => run.Id).ValueGeneratedNever();
        builder.Property(run => run.Title).IsRequired();
        builder.Property(run => run.BodySnapshot).IsRequired();
        builder.ComplexProperty(run => run.ParentIssue, IssueRefColumns.Configure);
        builder.Property(run => run.Attention).HasConversion<AttentionReasonConverter>();
        builder.HasOne<RepositoryRecord>().WithMany().HasForeignKey(run => run.RepositoryId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(run => new { run.RepositoryId, run.QueuePosition }).HasDatabaseName("IX_SpecRuns_Repository_QueuePosition");
        builder.HasIndex(run => run.Status).HasDatabaseName("IX_SpecRuns_Status");

        // Each active spec of a repository occupies its own slot; with the default limit of one, a second active spec cannot be claimed.
        builder.HasIndex(run => new { run.RepositoryId, run.MaxActiveSpecsSlot })
            .IsUnique()
            .HasFilter(FilteredIndexSql.ActiveSpecRun)
            .HasDatabaseName("UX_SpecRuns_ActiveSlotPerRepository");
    }
}

internal sealed class SpecDependencyConfiguration : IEntityTypeConfiguration<SpecDependency>
{
    public void Configure(EntityTypeBuilder<SpecDependency> builder)
    {
        builder.ToTable("SpecDependencies");
        builder.HasKey(dependency => dependency.Id);
        builder.ComplexProperty(dependency => dependency.ExternalBlockingIssue, IssueRefColumns.Configure);
        builder.HasOne<SpecRun>().WithMany().HasForeignKey(dependency => dependency.BlockedSpecRunId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<SpecRun>().WithMany().HasForeignKey(dependency => dependency.BlockingSpecRunId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(dependency => new { dependency.BlockedSpecRunId, dependency.BlockingSpecRunId })
            .IsUnique()
            .HasFilter("\"BlockingSpecRunId\" IS NOT NULL")
            .HasDatabaseName("UX_SpecDependencies_Blocked_Blocking");
    }
}
