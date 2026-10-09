using System.Reflection;
using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Events;

namespace WebDevLoop.Core.Tests.Orchestration.TicketExecution;

/// <summary>
/// Committed workflow state shared by independent <see cref="CasWorkflowScope"/>s, mirroring one SQLite database used by
/// several EF Core scopes: every scope loads its own copies, saves compare-and-swap on <see cref="VersionedEntity.Version"/>,
/// and the filtered unique index allows one active implement/fix step per ticket. Single-threaded by design: concurrency
/// is modelled by interleaving async scopes (see <see cref="HoldSavesUntil"/>), not by parallel threads.
/// </summary>
internal sealed class CasWorkflowDatabase
{
    private static readonly MethodInfo CloneMethod =
        typeof(object).GetMethod("MemberwiseClone", BindingFlags.Instance | BindingFlags.NonPublic)!;

    private readonly Dictionary<object, VersionedEntity> _rows = [];
    private readonly List<TicketDependency> _ticketDependencies = [];
    private readonly List<WorkflowEvent> _committedEvents = [];
    private readonly List<(int Arrived, int Parties, TaskCompletionSource Released)> _barriers = [];
    private int _dispatchedEvents;
    private int _nextRepositoryId;

    public int SaveCount { get; private set; }

    public int ConflictCount { get; private set; }

    public IReadOnlyList<WorkflowEvent> CommittedEvents => _committedEvents;

    public CasWorkflowScope OpenScope() => new(this);

    /// <summary>The next <paramref name="parties"/> saves wait for each other, so every party loads before anyone commits.</summary>
    public void HoldSavesUntil(int parties) => _barriers.Add((0, parties, new TaskCompletionSource()));

    /// <summary>Committed events not yet handed out, in commit order (an outbox read + mark-dispatched).</summary>
    public IReadOnlyList<WorkflowEvent> TakeUndispatchedEvents()
    {
        WorkflowEvent[] pending = _committedEvents.Skip(_dispatchedEvents).ToArray();
        _dispatchedEvents = _committedEvents.Count;
        return pending;
    }

    public IEnumerable<T> Rows<T>() where T : VersionedEntity => _rows.Values.OfType<T>().Select(Clone);

    public IEnumerable<TicketDependency> TicketDependencies => _ticketDependencies;

    internal T? Load<T>(object key) where T : VersionedEntity => _rows.TryGetValue(key, out VersionedEntity? row) ? Clone((T)row) : null;

    internal IEnumerable<T> LoadAll<T>(Func<T, bool> predicate) where T : VersionedEntity =>
        _rows.Values.OfType<T>().Where(predicate).Select(Clone);

    internal async Task WaitForBarrierAsync()
    {
        if (_barriers.Count == 0)
        {
            return;
        }

        (int arrived, int parties, TaskCompletionSource released) = _barriers[0];
        arrived++;
        if (arrived == parties)
        {
            _barriers.RemoveAt(0);
            released.SetResult();
            return;
        }

        _barriers[0] = (arrived, parties, released);
        await released.Task;
    }

    internal static object KeyOf(VersionedEntity entity) => entity switch
    {
        RepositoryRecord repository => ("repository", repository.Id),
        SpecRun run => run.Id,
        TicketRun ticket => ticket.Id,
        StepRun step => step.Id,
        _ => throw new NotSupportedException($"{entity.GetType().Name} is not stored by {nameof(CasWorkflowDatabase)}."),
    };

    internal bool TryCommit(
        IReadOnlyCollection<(VersionedEntity Entity, int LoadedVersion)> modified,
        IReadOnlyCollection<VersionedEntity> added,
        IReadOnlyCollection<TicketDependency> addedDependencies,
        IReadOnlyCollection<WorkflowEvent> events)
    {
        bool versionsMatch = modified.All(row => _rows.TryGetValue(KeyOf(row.Entity), out VersionedEntity? committed) && committed.Version == row.LoadedVersion);
        bool keysFree = added.All(entity => entity is RepositoryRecord { Id: 0 } || !_rows.ContainsKey(KeyOf(entity)));
        if (!versionsMatch || !keysFree || !ImplementOrFixStepsStayUnique(modified, added))
        {
            ConflictCount++;
            return false;
        }

        foreach ((VersionedEntity entity, _) in modified)
        {
            entity.AdvanceVersion();
            _rows[KeyOf(entity)] = Clone(entity);
        }

        foreach (VersionedEntity entity in added)
        {
            if (entity is RepositoryRecord { Id: 0 } repository)
            {
                typeof(RepositoryRecord).GetProperty(nameof(RepositoryRecord.Id))!.SetValue(repository, ++_nextRepositoryId);
            }

            _rows[KeyOf(entity)] = Clone(entity);
        }

        _ticketDependencies.AddRange(addedDependencies);
        _committedEvents.AddRange(events);
        SaveCount++;
        return true;
    }

    internal static T Clone<T>(T entity) where T : class => (T)CloneMethod.Invoke(entity, null)!;

    private bool ImplementOrFixStepsStayUnique(
        IReadOnlyCollection<(VersionedEntity Entity, int LoadedVersion)> modified,
        IReadOnlyCollection<VersionedEntity> added)
    {
        Dictionary<StepRunId, StepRun> steps = _rows.Values.OfType<StepRun>().ToDictionary(step => step.Id);
        foreach (StepRun step in modified.Select(row => row.Entity).Concat(added).OfType<StepRun>())
        {
            steps[step.Id] = step;
        }

        return steps.Values
            .Where(step => step.IsActive && step.Kind.IsImplementOrFix() && step.TicketRunId is not null)
            .GroupBy(step => step.TicketRunId)
            .All(group => group.Count() == 1);
    }
}
