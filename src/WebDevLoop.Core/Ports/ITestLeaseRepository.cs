using WebDevLoop.Core.Domain;

namespace WebDevLoop.Core.Ports;

public interface ITestLeaseRepository
{
    Task<TestLease?> FindActiveAsync(RunId specRunId, CancellationToken cancellationToken);

    Task<IReadOnlyList<TestLease>> ListActiveAsync(CancellationToken cancellationToken);

    void Add(TestLease lease);
}
