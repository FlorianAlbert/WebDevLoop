using WebDevLoop.Core.Domain;

namespace WebDevLoop.Core.Ports;

public interface IFindingIssuanceRepository
{
    Task<FindingIssuance?> FindAsync(RunId specRunId, FindingFingerprint fingerprint, CancellationToken cancellationToken);

    Task<IReadOnlyList<FindingIssuance>> ListBySpecRunAsync(RunId specRunId, CancellationToken cancellationToken);

    void Add(FindingIssuance issuance);
}
