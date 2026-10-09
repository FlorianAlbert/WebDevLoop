using Bunit;
using Microsoft.Extensions.DependencyInjection;
using WebDevLoop.Core.Agents;
using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Management;
using WebDevLoop.Core.Ports;
using WebDevLoop.Core.Queries;
using WebDevLoop.Core.Orchestration.Control;
using WebDevLoop.Infrastructure.Prerequisites;
using WebDevLoop.Web.Tests.Api;

namespace WebDevLoop.Web.Tests.Components.Support;

internal sealed class FakeRunQueries : IRunQueries
{
    public List<SpecRunView> Specs { get; } = [];

    public List<TicketRunView> Tickets { get; } = [];

    public List<StepRunView> Steps { get; } = [];

    public List<RunEventView> Events { get; } = [];

    public List<StackLayerView> Stack { get; } = [];

    public Dictionary<string, List<SpecDependencyView>> Dependencies { get; } = [];

    /// <summary>Latest saga per ticket run id.</summary>
    public Dictionary<string, IntegrationSagaView> Sagas { get; } = [];

    public IntegrationSagaView Saga(string ticketId, IntegrationSagaCheckpoint checkpoint) =>
        Sagas[ticketId] = new IntegrationSagaView(ticketId, checkpoint, $"stack/run-1/{ticketId}", null, null, Views.Now);

    public Task<IReadOnlyList<SpecRunView>> ListSpecRunsAsync(int repositoryId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<SpecRunView>>(Specs.Where(spec => spec.RepositoryId == repositoryId).ToList());

    public Task<SpecRunView?> GetSpecRunAsync(RunId specRunId, CancellationToken cancellationToken) =>
        Task.FromResult(Specs.FirstOrDefault(spec => spec.Id == specRunId.Value));

    public Task<IReadOnlyList<TicketRunView>> ListTicketRunsAsync(RunId specRunId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<TicketRunView>>(Tickets.Where(ticket => ticket.SpecRunId == specRunId.Value).ToList());

    public Task<TicketRunView?> GetTicketRunAsync(TicketRunId ticketRunId, CancellationToken cancellationToken) =>
        Task.FromResult(Tickets.FirstOrDefault(ticket => ticket.Id == ticketRunId.Value));

    public Task<IReadOnlyList<StepRunView>> ListStepsAsync(TicketRunId ticketRunId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<StepRunView>>(Steps.Where(step => step.TicketRunId == ticketRunId.Value).ToList());

    public Task<StepRunView?> GetStepAsync(StepRunId stepRunId, CancellationToken cancellationToken) =>
        Task.FromResult(Steps.FirstOrDefault(step => step.Id == stepRunId.Value));

    public Task<IReadOnlyList<RunEventView>> ListEventsAsync(RunId specRunId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<RunEventView>>(Events.Where(e => e.SpecRunId == specRunId.Value).ToList());

    public Task<IReadOnlyList<StackLayerView>> ListStackAsync(RunId specRunId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<StackLayerView>>(Stack.ToList());

    public Task<IReadOnlyList<SpecDependencyView>> ListSpecDependenciesAsync(RunId specRunId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<SpecDependencyView>>(Dependencies.GetValueOrDefault(specRunId.Value) ?? []);

    public Task<IReadOnlyDictionary<string, IReadOnlyList<SpecDependencyView>>> ListSpecDependenciesForRepositoryAsync(int repositoryId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyDictionary<string, IReadOnlyList<SpecDependencyView>>>(
            Dependencies.Where(entry => Specs.Any(spec => spec.Id == entry.Key && spec.RepositoryId == repositoryId))
                .ToDictionary(entry => entry.Key, entry => (IReadOnlyList<SpecDependencyView>)entry.Value));

    public Task<IntegrationSagaView?> GetLatestSagaAsync(TicketRunId ticketRunId, CancellationToken cancellationToken) =>
        Task.FromResult(Sagas.GetValueOrDefault(ticketRunId.Value));

    public Task<IReadOnlyDictionary<string, IntegrationSagaView>> ListLatestSagasAsync(RunId specRunId, CancellationToken cancellationToken)
    {
        HashSet<string> tickets = Tickets.Where(ticket => ticket.SpecRunId == specRunId.Value).Select(ticket => ticket.Id).ToHashSet();
        return Task.FromResult<IReadOnlyDictionary<string, IntegrationSagaView>>(
            Sagas.Where(entry => tickets.Contains(entry.Key)).ToDictionary(entry => entry.Key, entry => entry.Value));
    }

    public void Replace(TicketRunView updated)
    {
        Tickets[Tickets.FindIndex(ticket => ticket.Id == updated.Id)] = updated;
    }
}

internal sealed class FakeAgentLogReader : IAgentLogReader, IAgentLogNotifications
{
    private readonly List<(StepRunId Step, Action Handler)> _subscribers = [];

    private readonly object _gate = new();
    private readonly List<AgentLogView> _entries = [];
    private readonly List<int> _afterSequences = [];

    public int ReadCalls
    {
        get
        {
            lock (_gate)
            {
                return _afterSequences.Count;
            }
        }
    }

    public IReadOnlyList<int> AfterSequences
    {
        get
        {
            lock (_gate)
            {
                return _afterSequences.ToList();
            }
        }
    }

    public int SubscriberCount
    {
        get
        {
            lock (_gate)
            {
                return _subscribers.Count;
            }
        }
    }

    /// <summary>Signals subscribers of the step the way the persistent store does after a flush; entries are added with <see cref="Append"/>.</summary>
    public void NotifyFlushed(string stepRunId)
    {
        Action[] handlers;
        lock (_gate)
        {
            handlers = _subscribers.Where(subscriber => subscriber.Step.Value == stepRunId).Select(subscriber => subscriber.Handler).ToArray();
        }

        foreach (Action handler in handlers)
        {
            handler();
        }
    }

    public IDisposable Subscribe(StepRunId stepRunId, Action onEntriesAvailable)
    {
        lock (_gate)
        {
            _subscribers.Add((stepRunId, onEntriesAvailable));
        }

        return new Unsubscriber(() =>
        {
            lock (_gate)
            {
                _subscribers.RemoveAll(subscriber => subscriber.Handler == onEntriesAvailable);
            }
        });
    }

    public void Append(string text, AgentLogKind kind = AgentLogKind.Assistant)
    {
        lock (_gate)
        {
            _entries.Add(new AgentLogView(_entries.Count + 1, Views.Now, kind, text));
        }
    }

    public Task<IReadOnlyList<AgentLogView>> ReadAsync(StepRunId stepRunId, int afterSequence, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            _afterSequences.Add(afterSequence);
            return Task.FromResult<IReadOnlyList<AgentLogView>>(_entries.Where(entry => entry.Sequence > afterSequence).ToList());
        }
    }
}

internal sealed class Unsubscriber(Action unsubscribe) : IDisposable
{
    public void Dispose() => unsubscribe();
}

internal sealed class FakeSettingsManager : ISettingsManager
{
    public Dictionary<AgentRole, EffectiveRoleSettingsView> Roles { get; } = Enum.GetValues<AgentRole>()
        .ToDictionary(role => role, role => new EffectiveRoleSettingsView($"model-{role}", "high", "prompt", 600));

    public Task<CommandResult<EffectiveSettingsView>> GetEffectiveAsync(int repositoryId, CancellationToken cancellationToken) =>
        Task.FromResult(CommandResult<EffectiveSettingsView>.Succeeded(new EffectiveSettingsView(
            "/ws", "/copilot", "main", 1, SpecDependencyMode.WaitForMerge, 2, 1, 3, 2, 2, 2, "run it", new PortRangeData(5000, 5010), Roles)));

    public Task<SettingsProfileData> GetGlobalAsync(CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<CommandResult<SettingsProfileData>> SaveGlobalAsync(SettingsProfileData data, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<CommandResult<SettingsProfileData>> GetRepositoryAsync(int repositoryId, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<CommandResult<SettingsProfileData>> SaveRepositoryAsync(int repositoryId, SettingsProfileData data, CancellationToken cancellationToken) => throw new NotSupportedException();
}

internal sealed class RunDetailHarness : BunitContext
{
    public RunDetailHarness()
    {
        Queries = new FakeRunQueries();
        Logs = new FakeAgentLogReader();
        Settings = new FakeSettingsManager();
        Bus = Events.NewBus();
        Services.AddSingleton<IRunQueries>(Queries);
        Services.AddSingleton<IAgentLogReader>(Logs);
        Services.AddSingleton<IAgentLogNotifications>(Logs);
        Services.AddSingleton<ISettingsManager>(Settings);
        Services.AddSingleton<WebDevLoop.Core.Events.IRunEventBus>(Bus);
        Services.AddSingleton<IRunControl>(Control);
        Readiness = new DiagnosticReadiness(Validator, new FakeClock());
        Readiness.RefreshAsync(CancellationToken.None).GetAwaiter().GetResult();
        Services.AddSingleton(Readiness);
    }

    public FakeRunQueries Queries { get; }

    public FakeRunControl Control { get; } = new();

    public FakePrerequisiteValidator Validator { get; } = new();

    /// <summary>Operational unless <see cref="EnterDiagnosticModeAsync"/> is called.</summary>
    public DiagnosticReadiness Readiness { get; }

    public async Task EnterDiagnosticModeAsync()
    {
        Validator.Checks = [new PrerequisiteCheck("GitHub auth", PrerequisiteStatus.Failed, "No GitHub App configured", "Configure a GitHub App or PAT")];
        await Readiness.RefreshAsync(CancellationToken.None);
    }

    public FakeAgentLogReader Logs { get; }

    public FakeSettingsManager Settings { get; }

    public WebDevLoop.Infrastructure.Events.InProcessRunEventBus Bus { get; }
}
