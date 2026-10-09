using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Ports;
using WebDevLoop.Core.Tests.Orchestration.Findings;
using WebDevLoop.Core.Tests.Orchestration.TicketExecution;

namespace WebDevLoop.Core.Tests.Orchestration.Completion.Testing;

/// <summary>Committed <see cref="TestLease"/> rows with the real schema's unique indexes: one active lease per run and per port.</summary>
internal sealed class TestLeaseStore
{
    private readonly Dictionary<long, TestLease> _rows = [];
    private long _nextId;

    public IReadOnlyList<TestLease> Rows => _rows.Values.Select(CasWorkflowDatabase.Clone).OrderBy(lease => lease.Id).ToArray();

    /// <summary>Commits a lease outside any workflow scope (e.g. one left behind by a previous app process).</summary>
    public TestLease Seed(TestLease lease)
    {
        Commit([lease]);
        return lease;
    }

    internal IEnumerable<TestLease> LoadActive() => _rows.Values.Where(lease => lease.IsActive).Select(CasWorkflowDatabase.Clone);

    internal bool Violates(IReadOnlyCollection<TestLease> added) =>
        added.Any(lease => _rows.Values.Any(row => row.IsActive && (row.SpecRunId == lease.SpecRunId || row.Port == lease.Port)));

    internal void Commit(IEnumerable<TestLease> leases)
    {
        foreach (TestLease lease in leases)
        {
            if (lease.Id == 0)
            {
                typeof(TestLease).GetProperty(nameof(TestLease.Id))!.SetValue(lease, ++_nextId);
            }

            _rows[lease.Id] = CasWorkflowDatabase.Clone(lease);
        }
    }
}

/// <summary>
/// One unit-of-work scope over the workflow database, finding issuances, and test leases: lease changes commit only
/// together with the workflow changes, and a second active lease for a run or port loses like a unique-index violation.
/// </summary>
internal sealed class TestingWorkflowScope(FindingWorkflowScope findings, TestLeaseStore store) : ITestLeaseRepository, IUnitOfWork
{
    private readonly List<TestLease> _tracked = [];
    private readonly List<TestLease> _added = [];

    public FindingWorkflowScope Findings { get; } = findings;

    public CasWorkflowScope Workflow => Findings.Workflow;

    public Task<TestLease?> FindActiveAsync(RunId specRunId, CancellationToken cancellationToken) =>
        Task.FromResult(_tracked.FirstOrDefault(lease => lease.IsActive && lease.SpecRunId == specRunId)
            ?? Track(store.LoadActive().FirstOrDefault(lease => lease.SpecRunId == specRunId && _tracked.All(tracked => tracked.Id != lease.Id))));

    public Task<IReadOnlyList<TestLease>> ListActiveAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<TestLease>>(store.LoadActive()
            .Select(loaded => _tracked.FirstOrDefault(lease => lease.Id == loaded.Id) ?? Track(loaded)!)
            .Where(lease => lease.IsActive)
            .ToArray());

    public void Add(TestLease lease) => _added.Add(lease);

    public async Task<SaveOutcome> SaveChangesAsync(CancellationToken cancellationToken)
    {
        if (store.Violates(_added) || await Findings.SaveChangesAsync(cancellationToken) != SaveOutcome.Saved)
        {
            _tracked.Clear();
            _added.Clear();
            return SaveOutcome.ConcurrencyConflict;
        }

        store.Commit(_tracked.Concat(_added));
        _tracked.AddRange(_added);
        _added.Clear();
        return SaveOutcome.Saved;
    }

    private TestLease? Track(TestLease? loaded)
    {
        if (loaded is not null)
        {
            _tracked.Add(loaded);
        }

        return loaded;
    }
}
