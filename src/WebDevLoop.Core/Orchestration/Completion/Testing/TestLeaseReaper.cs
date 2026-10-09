using System.Globalization;
using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Ports;

namespace WebDevLoop.Core.Orchestration.Completion.Testing;

/// <summary>
/// Restart recovery for tester leases: before schedulers start, no tester of this process is running yet, so every lease
/// still active was left behind by a previous process. Its leftover processes (the application the tester started, its
/// browser, its shells) are killed and the lease is released, freeing the port and the spec for a new tester attempt.
/// </summary>
public sealed class TestLeaseReaper(ITestLeaseRepository leases, ITestTargetRunner targets, IUnitOfWork unitOfWork, IClock clock)
{
    /// <returns>The number of leases stopped and released.</returns>
    public async Task<int> StopOrphanedAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<TestLease> orphaned = await leases.ListActiveAsync(cancellationToken);
        foreach (TestLease lease in orphaned)
        {
            await targets.StopAsync(TargetOf(lease), cancellationToken);
            lease.Release(clock.UtcNow);
        }

        if (orphaned.Count > 0 && await unitOfWork.SaveChangesAsync(cancellationToken) != SaveOutcome.Saved)
        {
            throw new InvalidOperationException("Releasing orphaned test leases lost a concurrent update; no tester may run before recovery completes.");
        }

        return orphaned.Count;
    }

    /// <summary>The supervisor identifies a target's processes by spec run and port; URL and environment are not needed to stop it.</summary>
    internal static TestTarget TargetOf(TestLease lease) => new(
        lease.SpecRunId,
        lease.Port,
        new Uri(string.Create(CultureInfo.InvariantCulture, $"http://localhost:{lease.Port}/")),
        new Dictionary<string, string>());
}
