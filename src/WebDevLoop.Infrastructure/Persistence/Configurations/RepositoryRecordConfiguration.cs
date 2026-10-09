using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WebDevLoop.Core.Domain;

namespace WebDevLoop.Infrastructure.Persistence.Configurations;

internal sealed class RepositoryRecordConfiguration : IEntityTypeConfiguration<RepositoryRecord>
{
    // GitHub owner and repository names are case-insensitive.
    private const string CaseInsensitive = "NOCASE";

    public void Configure(EntityTypeBuilder<RepositoryRecord> builder)
    {
        builder.ToTable("Repositories");
        builder.HasKey(repository => repository.Id);
        builder.Property(repository => repository.Owner).IsRequired().UseCollation(CaseInsensitive);
        builder.Property(repository => repository.Name).IsRequired().UseCollation(CaseInsensitive);
        builder.Property(repository => repository.CloneUrl).IsRequired();
        builder.Property(repository => repository.LocalPath).IsRequired();
        builder.HasIndex(repository => new { repository.Owner, repository.Name }).IsUnique().HasDatabaseName("UX_Repositories_Owner_Name");
    }
}
