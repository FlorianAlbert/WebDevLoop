using System.Reflection;
using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Events;
using WebDevLoop.Core.Ports;

namespace WebDevLoop.Core.Tests.Orchestration.TicketExecution;

/// <summary>
/// One unit-of-work scope over <see cref="CasWorkflowDatabase"/> (like one EF Core <c>DbContext</c>): loads are copies
/// tracked in an identity map, queries filter committed rows but return tracked instances, and a lost compare-and-swap
/// clears the tracker and drops appended events.
/// </summary>
internal sealed class CasWorkflowScope(CasWorkflowDatabase database) :
    IRepositoryRecordRepository,
    ISpecRunRepository,
    ITicketRunRepository,
    IStepRunRepository,
    IUnitOfWork,
    IOutbox
{
    private readonly Dictionary<object, (VersionedEntity Entity, VersionedEntity Snapshot)> _tracked = [];
    private readonly List<VersionedEntity> _added = [];
    private readonly List<TicketDependency> _addedDependencies = [];
    private readonly List<WorkflowEvent> _events = [];

    public async Task<SaveOutcome> SaveChangesAsync(CancellationToken cancellationToken)
    {
        await database.WaitForBarrierAsync();
        (VersionedEntity Entity, int LoadedVersion)[] modified = _tracked.Values
            .Where(row => !SameState(row.Entity, row.Snapshot))
            .Select(row => (row.Entity, row.Snapshot.Version))
            .ToArray();
        bool committed = database.TryCommit(modified, _added, _addedDependencies, _events);
        if (committed)
        {
            foreach (VersionedEntity entity in modified.Select(row => row.Entity).Concat(_added))
            {
                Track(entity);
            }
        }
        else
        {
            _tracked.Clear();
        }

        _added.Clear();
        _addedDependencies.Clear();
        _events.Clear();
        return committed ? SaveOutcome.Saved : SaveOutcome.ConcurrencyConflict;
    }

    public void Append(WorkflowEvent workflowEvent) => _events.Add(workflowEvent);

    Task<RepositoryRecord?> IRepositoryRecordRepository.GetAsync(int id, CancellationToken cancellationToken) =>
        Task.FromResult(Resolve(database.Load<RepositoryRecord>(("repository", id))));

    public Task<RepositoryRecord?> FindAsync(GitHubRepoRef repo, CancellationToken cancellationToken) =>
        Task.FromResult(Query<RepositoryRecord>(repository => repository.Ref == repo).FirstOrDefault());

    public Task<IReadOnlyList<RepositoryRecord>> ListAsync(CancellationToken cancellationToken) => List(Query<RepositoryRecord>(_ => true));

    public void Add(RepositoryRecord repository) => _added.Add(repository);

    public void Remove(RepositoryRecord repository) => throw new NotSupportedException();

    public Task<SpecRun?> GetAsync(RunId id, CancellationToken cancellationToken) => Task.FromResult(Resolve(database.Load<SpecRun>(id)));

    public Task<IReadOnlyList<SpecRun>> ListByRepositoryAsync(int repositoryId, CancellationToken cancellationToken) =>
        List(Query<SpecRun>(run => run.RepositoryId == repositoryId).OrderBy(run => run.QueuePosition));

    public Task<IReadOnlyList<SpecRun>> ListNonTerminalAsync(CancellationToken cancellationToken) => List(Query<SpecRun>(run => !run.IsTerminal));

    Task<IReadOnlyList<SpecDependency>> ISpecRunRepository.ListDependenciesAsync(RunId blockedSpecRunId, CancellationToken cancellationToken) =>
        List(Array.Empty<SpecDependency>());

    public void Add(SpecRun specRun) => _added.Add(specRun);

    public void AddDependency(SpecDependency dependency) => throw new NotSupportedException();

    public Task<TicketRun?> GetAsync(TicketRunId id, CancellationToken cancellationToken) => Task.FromResult(Resolve(database.Load<TicketRun>(id)));

    Task<IReadOnlyList<TicketRun>> ITicketRunRepository.ListBySpecRunAsync(RunId specRunId, CancellationToken cancellationToken) =>
        List(Query<TicketRun>(ticket => ticket.SpecRunId == specRunId));

    Task<IReadOnlyList<TicketDependency>> ITicketRunRepository.ListDependenciesAsync(RunId specRunId, CancellationToken cancellationToken) =>
        List(database.TicketDependencies.Where(dependency => dependency.SpecRunId == specRunId));

    /// <summary>Reads committed rows directly, bypassing this scope's identity map (an untracked query).</summary>
    public Task<ImplementerSlotUsage> CountOccupiedImplementerSlotsAsync(int repositoryId, CancellationToken cancellationToken)
    {
        Dictionary<RunId, int> repositoryOf = database.LoadAll<SpecRun>(run => !run.IsTerminal).ToDictionary(run => run.Id, run => run.RepositoryId);
        int[] occupied = database.LoadAll<TicketRun>(ticket => ticket.Status.OccupiesImplementerSlot() && repositoryOf.ContainsKey(ticket.SpecRunId))
            .Select(ticket => repositoryOf[ticket.SpecRunId])
            .ToArray();
        return Task.FromResult(new ImplementerSlotUsage(occupied.Length, occupied.Count(id => id == repositoryId)));
    }

    public void Add(TicketRun ticketRun) => _added.Add(ticketRun);

    public void AddDependency(TicketDependency dependency) => _addedDependencies.Add(dependency);

    public Task<StepRun?> GetAsync(StepRunId id, CancellationToken cancellationToken) => Task.FromResult(Resolve(database.Load<StepRun>(id)));

    Task<IReadOnlyList<StepRun>> IStepRunRepository.ListBySpecRunAsync(RunId specRunId, CancellationToken cancellationToken) =>
        List(Query<StepRun>(step => step.SpecRunId == specRunId));

    public Task<IReadOnlyList<StepRun>> ListByTicketRunAsync(TicketRunId ticketRunId, CancellationToken cancellationToken) =>
        List(Query<StepRun>(step => step.TicketRunId == ticketRunId));

    public Task<IReadOnlyList<StepRun>> ListActiveAsync(CancellationToken cancellationToken) => List(Query<StepRun>(step => step.IsActive));

    public void Add(StepRun stepRun) => _added.Add(stepRun);

    public Task<IReadOnlyList<EventEnvelope>> ReadPendingAsync(int maxCount, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task MarkDispatchedAsync(long messageId, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task RecordFailureAsync(long messageId, string error, CancellationToken cancellationToken) => throw new NotSupportedException();

    private IEnumerable<T> Query<T>(Func<T, bool> predicate) where T : VersionedEntity =>
        database.LoadAll(predicate).Select(row => Resolve(row)!).ToArray();

    /// <summary>Identity resolution: an already tracked instance wins over the freshly loaded copy (as in EF Core).</summary>
    private T? Resolve<T>(T? loaded) where T : VersionedEntity
    {
        if (loaded is null)
        {
            return null;
        }

        object key = CasWorkflowDatabase.KeyOf(loaded);
        if (_tracked.TryGetValue(key, out (VersionedEntity Entity, VersionedEntity Snapshot) tracked))
        {
            return (T)tracked.Entity;
        }

        Track(loaded);
        return loaded;
    }

    private void Track(VersionedEntity entity) => _tracked[CasWorkflowDatabase.KeyOf(entity)] = (entity, CasWorkflowDatabase.Clone(entity));

    private static bool SameState(VersionedEntity current, VersionedEntity snapshot) =>
        current.GetType()
            .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Where(property => property.CanRead && property.GetIndexParameters().Length == 0)
            .All(property => Equals(property.GetValue(current), property.GetValue(snapshot)));

    private static Task<IReadOnlyList<T>> List<T>(IEnumerable<T> items) => Task.FromResult<IReadOnlyList<T>>(items.ToArray());
}
