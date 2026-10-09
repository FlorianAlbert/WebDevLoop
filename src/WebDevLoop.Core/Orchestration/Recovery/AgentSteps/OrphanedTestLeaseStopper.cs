using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Orchestration.Completion.Testing;
using WebDevLoop.Core.Ports;

namespace WebDevLoop.Core.Orchestration.Recovery.AgentSteps;

/// <summary>
/// Kills leftover tester processes of leases no live tester owns — leases acquired by a previous process, and leases that
/// outlived their time-to-live (the tester's timeout plus grace, so its runner is hung) — and releases them, freeing the
/// port and the spec for a new tester attempt. Leases of live testers of this process are left alone, so this is safe to
/// run periodically. This is the only stopper of orphaned tester processes; startup and periodic recovery both reach it
/// through <see cref="AgentStepRecoveryService"/>.
/// </summary>
public sealed class OrphanedTestLeaseStopper(ITestLeaseRepository leases, ITestTargetRunner targets, IUnitOfWork unitOfWork, IClock clock, ProcessBoot boot)
{
    /// <returns>The leases stopped and released; a lease another writer changed first is left to the next pass.</returns>
    public async Task<IReadOnlyList<StoppedTestLease>> StopAsync(CancellationToken cancellationToken)
    {
        DateTimeOffset now = clock.UtcNow;
        RunId[] orphanedSpecs = (await leases.ListActiveAsync(cancellationToken))
            .Where(lease => IsOrphaned(lease, now))
            .Select(lease => lease.SpecRunId)
            .ToArray();
        var stopped = new List<StoppedTestLease>();
        foreach (RunId specRunId in orphanedSpecs)
        {
            // Reloaded per lease: a lost save clears the unit of work.
            if (await leases.FindActiveAsync(specRunId, cancellationToken) is { } lease && IsOrphaned(lease, now)
                && await StopAsync(lease, now, cancellationToken) is { } released)
            {
                stopped.Add(released);
            }
        }

        return stopped;
    }

    private bool IsOrphaned(TestLease lease, DateTimeOffset now) => lease.IsExpiredAt(now) || boot.IsBeforeBoot(lease.AcquiredAt);

    private async Task<StoppedTestLease?> StopAsync(TestLease lease, DateTimeOffset now, CancellationToken cancellationToken)
    {
        bool expired = lease.IsExpiredAt(now);
        TestTargetStopResult result = await targets.StopAsync(TestLeaseTarget.Of(lease), cancellationToken);
        lease.Release(now);
        return await unitOfWork.SaveChangesAsync(cancellationToken) == SaveOutcome.Saved
            ? new StoppedTestLease(lease.SpecRunId, lease.Port, expired, result.TerminatedProcessCount)
            : null;
    }
}
