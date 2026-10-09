using Bunit;
using Microsoft.Extensions.DependencyInjection;
using WebDevLoop.Core.Agents;
using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Management;
using WebDevLoop.Core.Ports;
using WebDevLoop.Core.Queries;

namespace WebDevLoop.Web.Tests.Components.Support;

internal sealed class FakeRunQueries : IRunQueries
{
    public List<SpecRunView> Specs { get; } = [];

    public List<TicketRunView> Tickets { get; } = [];

    public List<StepRunView> Steps { get; } = [];

    public List<RunEventView> Events { get; } = [];

    public List<StackLayerView> Stack { get; } = [];

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

    public void Replace(TicketRunView updated)
    {
        Tickets[Tickets.FindIndex(ticket => ticket.Id == updated.Id)] = updated;
    }
}

internal sealed class FakeAgentLogReader : IAgentLogReader
{
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

internal sealed class FakeSagaRepository : IIntegrationSagaRepository
{
    public Dictionary<string, IntegrationSaga> Sagas { get; } = [];

    public IntegrationSaga Start(string runId, string ticketId)
    {
        IntegrationSaga saga = IntegrationSaga.Start(new RunId(runId), new TicketRunId(ticketId), null, Views.Now);
        Sagas[ticketId] = saga;
        return saga;
    }

    public Task<IntegrationSaga?> FindLatestForTicketAsync(TicketRunId ticketRunId, CancellationToken cancellationToken) =>
        Task.FromResult(Sagas.GetValueOrDefault(ticketRunId.Value));

    public Task<IReadOnlyList<IntegrationSaga>> ListIncompleteAsync(CancellationToken cancellationToken) => throw new NotSupportedException();

    public void Add(IntegrationSaga saga) => throw new NotSupportedException();
}

internal sealed class FakeSettingsManager : ISettingsManager
{
    public Dictionary<AgentRole, EffectiveRoleSettingsView> Roles { get; } = Enum.GetValues<AgentRole>()
        .ToDictionary(role => role, role => new EffectiveRoleSettingsView($"model-{role}", "high", "prompt", 600));

    public Task<CommandResult<EffectiveSettingsView>> GetEffectiveAsync(int repositoryId, CancellationToken cancellationToken) =>
        Task.FromResult(CommandResult<EffectiveSettingsView>.Succeeded(new EffectiveSettingsView(
            "/ws", "/copilot", "main", 1, SpecDependencyMode.WaitForMerge, 2, 1, 3, 2, 2, 2, "run it", new PortRangeData(5000, 5010), false, Roles)));

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
        Sagas = new FakeSagaRepository();
        Settings = new FakeSettingsManager();
        Bus = Events.NewBus();
        Services.AddSingleton<IRunQueries>(Queries);
        Services.AddSingleton<IAgentLogReader>(Logs);
        Services.AddSingleton<IIntegrationSagaRepository>(Sagas);
        Services.AddSingleton<ISettingsManager>(Settings);
        Services.AddSingleton<WebDevLoop.Core.Events.IRunEventBus>(Bus);
    }

    public FakeRunQueries Queries { get; }

    public FakeAgentLogReader Logs { get; }

    public FakeSagaRepository Sagas { get; }

    public FakeSettingsManager Settings { get; }

    public WebDevLoop.Infrastructure.Events.InProcessRunEventBus Bus { get; }
}
