using Microsoft.EntityFrameworkCore;
using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Ports;

namespace WebDevLoop.Infrastructure.Persistence.Repositories;

public sealed class EfTestLeaseRepository(WebDevLoopDbContext context) : ITestLeaseRepository
{
    public async Task<TestLease?> FindActiveAsync(RunId specRunId, CancellationToken cancellationToken) =>
        await context.TestLeases.FirstOrDefaultAsync(
            lease => lease.SpecRunId == specRunId && lease.ReleasedAt == null,
            cancellationToken);

    public async Task<IReadOnlyList<TestLease>> ListActiveAsync(CancellationToken cancellationToken) =>
        await context.TestLeases
            .Where(lease => lease.ReleasedAt == null)
            .OrderBy(lease => lease.Id)
            .ToListAsync(cancellationToken);

    public void Add(TestLease lease) => context.TestLeases.Add(lease);
}
