using Microsoft.EntityFrameworkCore;
using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Ports;

namespace WebDevLoop.Infrastructure.Persistence.Repositories;

public sealed class EfFindingIssuanceRepository(WebDevLoopDbContext context) : IFindingIssuanceRepository
{
    public async Task<FindingIssuance?> FindAsync(RunId specRunId, FindingFingerprint fingerprint, CancellationToken cancellationToken) =>
        await context.FindingIssuances.FirstOrDefaultAsync(
            finding => finding.SpecRunId == specRunId && finding.Fingerprint == fingerprint,
            cancellationToken);

    public async Task<IReadOnlyList<FindingIssuance>> ListBySpecRunAsync(RunId specRunId, CancellationToken cancellationToken) =>
        await context.FindingIssuances
            .Where(finding => finding.SpecRunId == specRunId)
            .OrderBy(finding => finding.Id)
            .ToListAsync(cancellationToken);

    public void Add(FindingIssuance issuance) => context.FindingIssuances.Add(issuance);
}
