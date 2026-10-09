using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Ports;
using WebDevLoop.Core.Tests.Orchestration.TicketExecution;

namespace WebDevLoop.Core.Tests.Orchestration.Findings;

/// <summary>Committed <see cref="FindingIssuance"/> rows with the unique (spec run, fingerprint) index of the real schema.</summary>
internal sealed class FindingIssuanceStore
{
    private readonly Dictionary<(RunId, FindingFingerprint), FindingIssuance> _rows = [];

    public IReadOnlyList<FindingIssuance> Rows => _rows.Values.Select(CasWorkflowDatabase.Clone).ToArray();

    internal FindingIssuance? Load(RunId specRunId, FindingFingerprint fingerprint) =>
        _rows.TryGetValue((specRunId, fingerprint), out FindingIssuance? row) ? CasWorkflowDatabase.Clone(row) : null;

    internal IEnumerable<FindingIssuance> LoadAll(RunId specRunId) =>
        _rows.Values.Where(row => row.SpecRunId == specRunId).Select(CasWorkflowDatabase.Clone);

    internal bool Contains(FindingIssuance issuance) => _rows.ContainsKey(KeyOf(issuance));

    internal void Commit(FindingIssuance issuance) => _rows[KeyOf(issuance)] = CasWorkflowDatabase.Clone(issuance);

    private static (RunId, FindingFingerprint) KeyOf(FindingIssuance issuance) => (issuance.SpecRunId, issuance.Fingerprint);
}

/// <summary>
/// One unit-of-work scope over the CAS workflow database plus finding issuances: issuance changes commit only together
/// with the workflow changes, and planning an already planned fingerprint loses like a unique-index violation.
/// </summary>
internal sealed class FindingWorkflowScope(CasWorkflowScope workflow, FindingIssuanceStore store) : IFindingIssuanceRepository, IUnitOfWork
{
    private readonly List<FindingIssuance> _tracked = [];
    private readonly List<FindingIssuance> _added = [];

    public CasWorkflowScope Workflow { get; } = workflow;

    public Task<FindingIssuance?> FindAsync(RunId specRunId, FindingFingerprint fingerprint, CancellationToken cancellationToken) =>
        Task.FromResult(_tracked.FirstOrDefault(row => row.SpecRunId == specRunId && row.Fingerprint == fingerprint)
            ?? Track(store.Load(specRunId, fingerprint)));

    public Task<IReadOnlyList<FindingIssuance>> ListBySpecRunAsync(RunId specRunId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<FindingIssuance>>(store.LoadAll(specRunId)
            .Select(loaded => _tracked.FirstOrDefault(row => row.Fingerprint == loaded.Fingerprint && row.SpecRunId == specRunId) ?? Track(loaded)!)
            .ToArray());

    public void Add(FindingIssuance issuance) => _added.Add(issuance);

    public async Task<SaveOutcome> SaveChangesAsync(CancellationToken cancellationToken)
    {
        if (_added.Any(store.Contains) || await Workflow.SaveChangesAsync(cancellationToken) != SaveOutcome.Saved)
        {
            _tracked.Clear();
            _added.Clear();
            return SaveOutcome.ConcurrencyConflict;
        }

        foreach (FindingIssuance issuance in _tracked.Concat(_added))
        {
            store.Commit(issuance);
        }

        _tracked.AddRange(_added);
        _added.Clear();
        return SaveOutcome.Saved;
    }

    private FindingIssuance? Track(FindingIssuance? loaded)
    {
        if (loaded is not null)
        {
            _tracked.Add(loaded);
        }

        return loaded;
    }
}
