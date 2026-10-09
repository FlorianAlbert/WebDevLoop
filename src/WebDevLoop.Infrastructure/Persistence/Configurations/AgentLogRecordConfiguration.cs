using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace WebDevLoop.Infrastructure.Persistence.Configurations;

/// <summary>
/// Append-only diagnostic log. There is deliberately no foreign key to the step: a log write must never fail a run,
/// and the (step, sequence) key serves both the paged "after N" read and the retention delete.
/// </summary>
internal sealed class AgentLogRecordConfiguration : IEntityTypeConfiguration<AgentLogRecord>
{
    public void Configure(EntityTypeBuilder<AgentLogRecord> builder)
    {
        builder.ToTable("AgentLogEntries");
        builder.HasKey(entry => new { entry.StepRunId, entry.Sequence });
        builder.Property(entry => entry.Text).IsRequired();
    }
}
