using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace WebDevLoop.Infrastructure.Persistence;

/// <summary>Used by <c>dotnet ef</c> only; the connection string is irrelevant for scaffolding migrations.</summary>
internal sealed class WebDevLoopDbContextFactory : IDesignTimeDbContextFactory<WebDevLoopDbContext>
{
    public WebDevLoopDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<WebDevLoopDbContext>().UseSqlite("Data Source=design-time.db").Options);
}
