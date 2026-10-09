using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WebDevLoop.Core.Domain;

namespace WebDevLoop.Infrastructure.Persistence.Configurations;

internal static class IssueRefColumns
{
    public static void Configure(ComplexPropertyBuilder<IssueRef> issue)
    {
        issue.Property(value => value.Owner).IsRequired();
        issue.Property(value => value.Repo).IsRequired();
        issue.Property(value => value.Number);
        issue.Property(value => value.NodeId);
        issue.Property(value => value.DatabaseId);
    }
}
