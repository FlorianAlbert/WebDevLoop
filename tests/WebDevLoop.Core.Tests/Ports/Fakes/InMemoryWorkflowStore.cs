using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Events;
using WebDevLoop.Core.Ports;

namespace WebDevLoop.Core.Tests.Ports.Fakes;

/// <summary>
/// In-memory database: repositories, unit of work, and outbox. Outbox events only become pending when a save succeeds,
/// mirroring the atomic state-change + outbox contract.
/// </summary>
public sealed class InMemoryWorkflowStore :
    IUnitOfWork,
    IRepositoryRecordRepository,
    ISpecRunRepository,
    ITicketRunRepository,
    IStepRunRepository,
    IIntegrationSagaRepository,
    IPullStackLayerRepository,
    IFindingIssuanceRepository,
    ITestLeaseRepository,
    ISettingsProfileRepository,
    IRunEventRepository,
    IOutbox
{
    private readonly List<RepositoryRecord> _repositories = [];
    private readonly List<SpecRun> _specRuns = [];
    private readonly List<SpecDependency> _specDependencies = [];
    private readonly List<TicketRun> _ticketRuns = [];
    private readonly List<TicketDependency> _ticketDependencies = [];
    private readonly List<StepRun> _steps = [];
    private readonly List<IntegrationSaga> _sagas = [];
    private readonly List<PullStackLayer> _layers = [];
    private readonly List<FindingIssuance> _findings = [];
    private readonly List<TestLease> _leases = [];
    private readonly List<SettingsProfile> _settings = [];
    private readonly List<RunEvent> _events = [];
    private readonly List<WorkflowEvent> _unsaved = [];
    private readonly Dictionary<long, (WorkflowEvent Event, bool Dispatched, int Failures)> _outbox = [];

    public int SaveCount { get; private set; }

    public bool ConflictOnNextSave { get; set; }

    public IEnumerable<WorkflowEvent> PendingEvents => _outbox.Values.Where(row => !row.Dispatched).Select(row => row.Event);

    public Task<SaveOutcome> SaveChangesAsync(CancellationToken cancellationToken)
    {
        if (ConflictOnNextSave)
        {
            ConflictOnNextSave = false;
            _unsaved.Clear();
            return Task.FromResult(SaveOutcome.ConcurrencyConflict);
        }

        foreach (WorkflowEvent workflowEvent in _unsaved)
        {
            _outbox[_outbox.Count + 1] = (workflowEvent, false, 0);
        }

        _unsaved.Clear();
        SaveCount++;
        return Task.FromResult(SaveOutcome.Saved);
    }

    Task<RepositoryRecord?> IRepositoryRecordRepository.GetAsync(int id, CancellationToken cancellationToken) =>
        Task.FromResult(_repositories.FirstOrDefault(repository => repository.Id == id));

    public Task<RepositoryRecord?> FindAsync(GitHubRepoRef repo, CancellationToken cancellationToken) =>
        Task.FromResult(_repositories.FirstOrDefault(repository => repository.Ref == repo));

    public Task<IReadOnlyList<RepositoryRecord>> ListAsync(CancellationToken cancellationToken) => List(_repositories);

    public void Add(RepositoryRecord repository) => _repositories.Add(repository);

    public void Remove(RepositoryRecord repository) => _repositories.Remove(repository);

    public Task<SpecRun?> GetAsync(RunId id, CancellationToken cancellationToken) =>
        Task.FromResult(_specRuns.FirstOrDefault(run => run.Id == id));

    public Task<IReadOnlyList<SpecRun>> ListByRepositoryAsync(int repositoryId, CancellationToken cancellationToken) =>
        List(_specRuns.Where(run => run.RepositoryId == repositoryId).OrderBy(run => run.QueuePosition));

    public Task<IReadOnlyList<SpecRun>> ListNonTerminalAsync(CancellationToken cancellationToken) =>
        List(_specRuns.Where(run => !run.IsTerminal));

    Task<IReadOnlyList<SpecDependency>> ISpecRunRepository.ListDependenciesAsync(RunId blockedSpecRunId, CancellationToken cancellationToken) =>
        List(_specDependencies.Where(dependency => dependency.BlockedSpecRunId == blockedSpecRunId));

    public void Add(SpecRun specRun) => _specRuns.Add(specRun);

    public void AddDependency(SpecDependency dependency) => _specDependencies.Add(dependency);

    public Task<TicketRun?> GetAsync(TicketRunId id, CancellationToken cancellationToken) =>
        Task.FromResult(_ticketRuns.FirstOrDefault(ticket => ticket.Id == id));

    Task<IReadOnlyList<TicketRun>> ITicketRunRepository.ListBySpecRunAsync(RunId specRunId, CancellationToken cancellationToken) =>
        List(_ticketRuns.Where(ticket => ticket.SpecRunId == specRunId));

    Task<IReadOnlyList<TicketDependency>> ITicketRunRepository.ListDependenciesAsync(RunId specRunId, CancellationToken cancellationToken) =>
        List(_ticketDependencies.Where(dependency => dependency.SpecRunId == specRunId));

    public Task<ImplementerSlotUsage> CountOccupiedImplementerSlotsAsync(int repositoryId, CancellationToken cancellationToken)
    {
        int[] occupied = _ticketRuns
            .Where(ticket => ticket.Status.OccupiesImplementerSlot())
            .Select(ticket => _specRuns.FirstOrDefault(run => run.Id == ticket.SpecRunId && !run.IsTerminal))
            .OfType<SpecRun>()
            .Select(run => run.RepositoryId)
            .ToArray();
        return Task.FromResult(new ImplementerSlotUsage(occupied.Length, occupied.Count(id => id == repositoryId)));
    }

    public void Add(TicketRun ticketRun) => _ticketRuns.Add(ticketRun);

    public void AddDependency(TicketDependency dependency) => _ticketDependencies.Add(dependency);

    public void RemoveDependency(TicketDependency dependency) => _ticketDependencies.Remove(dependency);

    public Task<StepRun?> GetAsync(StepRunId id, CancellationToken cancellationToken) =>
        Task.FromResult(_steps.FirstOrDefault(step => step.Id == id));

    Task<IReadOnlyList<StepRun>> IStepRunRepository.ListBySpecRunAsync(RunId specRunId, CancellationToken cancellationToken) =>
        List(_steps.Where(step => step.SpecRunId == specRunId));

    public Task<IReadOnlyList<StepRun>> ListByTicketRunAsync(TicketRunId ticketRunId, CancellationToken cancellationToken) =>
        List(_steps.Where(step => step.TicketRunId == ticketRunId));

    Task<IReadOnlyList<StepRun>> IStepRunRepository.ListActiveAsync(CancellationToken cancellationToken) =>
        List(_steps.Where(step => step.IsActive));

    public void Add(StepRun stepRun) => _steps.Add(stepRun);

    public Task<IntegrationSaga?> FindLatestForTicketAsync(TicketRunId ticketRunId, CancellationToken cancellationToken) =>
        Task.FromResult(_sagas.LastOrDefault(saga => saga.TicketRunId == ticketRunId));

    public Task<IReadOnlyList<IntegrationSaga>> ListIncompleteAsync(CancellationToken cancellationToken) =>
        List(_sagas.Where(saga => !saga.IsCompleted));

    public void Add(IntegrationSaga saga) => _sagas.Add(saga);

    Task<IReadOnlyList<PullStackLayer>> IPullStackLayerRepository.ListBySpecRunAsync(RunId specRunId, CancellationToken cancellationToken) =>
        List(_layers.Where(layer => layer.SpecRunId == specRunId).OrderBy(layer => layer.Position));

    public void Add(PullStackLayer layer) => _layers.Add(layer);

    public Task<FindingIssuance?> FindAsync(RunId specRunId, FindingFingerprint fingerprint, CancellationToken cancellationToken) =>
        Task.FromResult(_findings.FirstOrDefault(finding => finding.SpecRunId == specRunId && finding.Fingerprint == fingerprint));

    Task<IReadOnlyList<FindingIssuance>> IFindingIssuanceRepository.ListBySpecRunAsync(RunId specRunId, CancellationToken cancellationToken) =>
        List(_findings.Where(finding => finding.SpecRunId == specRunId));

    public void Add(FindingIssuance issuance) => _findings.Add(issuance);

    public Task<TestLease?> FindActiveAsync(RunId specRunId, CancellationToken cancellationToken) =>
        Task.FromResult(_leases.FirstOrDefault(lease => lease.SpecRunId == specRunId && lease.IsActive));

    Task<IReadOnlyList<TestLease>> ITestLeaseRepository.ListActiveAsync(CancellationToken cancellationToken) =>
        List(_leases.Where(lease => lease.IsActive));

    public void Add(TestLease lease) => _leases.Add(lease);

    public Task<SettingsProfile?> GetGlobalAsync(CancellationToken cancellationToken) =>
        Task.FromResult(_settings.FirstOrDefault(profile => profile.IsGlobal));

    public Task<SettingsProfile?> FindForRepositoryAsync(int repositoryId, CancellationToken cancellationToken) =>
        Task.FromResult(_settings.FirstOrDefault(profile => profile.RepositoryId == repositoryId));

    public void Add(SettingsProfile profile) => _settings.Add(profile);

    Task<IReadOnlyList<RunEvent>> IRunEventRepository.ListBySpecRunAsync(RunId specRunId, CancellationToken cancellationToken) =>
        List(_events.Where(runEvent => runEvent.SpecRunId == specRunId));

    public void Add(RunEvent runEvent) => _events.Add(runEvent);

    public void Append(WorkflowEvent workflowEvent) => _unsaved.Add(workflowEvent);

    public Task<IReadOnlyList<EventEnvelope>> ReadPendingAsync(int maxCount, CancellationToken cancellationToken) =>
        List(_outbox.Where(row => !row.Value.Dispatched).Take(maxCount).Select(row => new EventEnvelope(row.Key, row.Value.Event)));

    public Task MarkDispatchedAsync(long messageId, CancellationToken cancellationToken)
    {
        _outbox[messageId] = _outbox[messageId] with { Dispatched = true };
        return Task.CompletedTask;
    }

    public Task RecordFailureAsync(long messageId, string error, CancellationToken cancellationToken)
    {
        _outbox[messageId] = _outbox[messageId] with { Failures = _outbox[messageId].Failures + 1 };
        return Task.CompletedTask;
    }

    private static Task<IReadOnlyList<T>> List<T>(IEnumerable<T> items) => Task.FromResult<IReadOnlyList<T>>(items.ToArray());
}
