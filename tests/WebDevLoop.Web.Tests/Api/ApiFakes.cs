using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Management;
using WebDevLoop.Core.Orchestration.SpecQueue;
using WebDevLoop.Core.Ports;
using WebDevLoop.Core.Queries;

namespace WebDevLoop.Web.Tests.Api;

internal static class ApiData
{
    public static readonly DateTimeOffset Now = new(2026, 10, 9, 12, 0, 0, TimeSpan.Zero);

    public static RepositoryView Repository(int id = 1, string name = "widgets", bool enabled = true) =>
        new(id, "acme", name, "main", $"https://github.com/acme/{name}.git", $"/work/acme/{name}", enabled, Now, Now);

    public static SpecRunView SpecRun(string id = "run-1", int repositoryId = 1, SpecRunStatus status = SpecRunStatus.Queued, int position = 1) =>
        new(id, repositoryId, 10, "Spec title", status, position, null, $"webdevloop/{id}/integration", null, null, null, 0, 0, Now, null, null, null, null);

    public static TicketRunView TicketRun(string id = "t-1", string specRunId = "run-1", params string[] blockedBy) =>
        new(id, specRunId, 11, "Ticket title", TicketRunStatus.Ready, 0, 0, $"webdevloop/{specRunId}/{id}", null, null, null, null, null, blockedBy, Now, Now, null);

    public static StepRunView Step(string id = "s-1", string? ticketRunId = "t-1", string specRunId = "run-1") =>
        new(id, specRunId, ticketRunId, StepKind.Implement, AgentRole.Implementer, StepStatus.Running, 1, "copilot-1", "/wt", "branch", Now, null, Now.AddHours(1), "hash", null, null);
}

internal sealed class FakeRepositoryRegistry : IRepositoryRegistry
{
    public CommandResult<RepositoryView> RegisterResult { get; set; } = CommandResult<RepositoryView>.Succeeded(ApiData.Repository());

    public CommandResult<RepositoryView> UpdateResult { get; set; } = CommandResult<RepositoryView>.Succeeded(ApiData.Repository());

    public CommandResult<int> RemoveResult { get; set; } = CommandResult<int>.Succeeded(1);

    public List<RegisterRepositoryCommand> Registered { get; } = [];

    public List<(int Id, UpdateRepositoryCommand Command)> Updated { get; } = [];

    public List<int> Removed { get; } = [];

    public Task<CommandResult<RepositoryView>> RegisterAsync(RegisterRepositoryCommand command, CancellationToken cancellationToken)
    {
        Registered.Add(command);
        return Task.FromResult(RegisterResult);
    }

    public Task<CommandResult<RepositoryView>> UpdateAsync(int repositoryId, UpdateRepositoryCommand command, CancellationToken cancellationToken)
    {
        Updated.Add((repositoryId, command));
        return Task.FromResult(UpdateResult);
    }

    public Task<CommandResult<int>> RemoveAsync(int repositoryId, CancellationToken cancellationToken)
    {
        Removed.Add(repositoryId);
        return Task.FromResult(RemoveResult);
    }
}

internal sealed class FakeRepositoryQueries : IRepositoryQueries
{
    public List<RepositoryView> Repositories { get; } = [];

    public Task<IReadOnlyList<RepositoryView>> ListAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<RepositoryView>>(Repositories.ToArray());

    public Task<RepositoryView?> GetAsync(int repositoryId, CancellationToken cancellationToken) =>
        Task.FromResult(Repositories.FirstOrDefault(repository => repository.Id == repositoryId));
}

internal sealed class FakeSettingsManager : ISettingsManager
{
    public SettingsProfileData Global { get; set; } = new() { MaxRetries = 2 };

    public CommandResult<SettingsProfileData> SaveGlobalResult { get; set; } = CommandResult<SettingsProfileData>.Succeeded(new SettingsProfileData { MaxRetries = 5 });

    public CommandResult<SettingsProfileData> RepositoryResult { get; set; } = CommandResult<SettingsProfileData>.Succeeded(new SettingsProfileData { MaxActiveSpecsPerRepo = 3 });

    public CommandResult<EffectiveSettingsView> EffectiveResult { get; set; } = CommandResult<EffectiveSettingsView>.NotFound("none");

    public List<SettingsProfileData> SavedGlobal { get; } = [];

    public List<(int Id, SettingsProfileData Data)> SavedRepository { get; } = [];

    public Task<SettingsProfileData> GetGlobalAsync(CancellationToken cancellationToken) => Task.FromResult(Global);

    public Task<CommandResult<SettingsProfileData>> SaveGlobalAsync(SettingsProfileData data, CancellationToken cancellationToken)
    {
        SavedGlobal.Add(data);
        return Task.FromResult(SaveGlobalResult);
    }

    public Task<CommandResult<SettingsProfileData>> GetRepositoryAsync(int repositoryId, CancellationToken cancellationToken) => Task.FromResult(RepositoryResult);

    public Task<CommandResult<SettingsProfileData>> SaveRepositoryAsync(int repositoryId, SettingsProfileData data, CancellationToken cancellationToken)
    {
        SavedRepository.Add((repositoryId, data));
        return Task.FromResult(RepositoryResult);
    }

    public Task<CommandResult<EffectiveSettingsView>> GetEffectiveAsync(int repositoryId, CancellationToken cancellationToken) => Task.FromResult(EffectiveResult);
}

internal sealed class FakeSpecEnqueuer : ISpecEnqueuer
{
    public EnqueueResult Result { get; set; } = new(EnqueueOutcome.Queued, new RunId("run-42"));

    public List<(int RepositoryId, int IssueNumber)> Calls { get; } = [];

    public Task<EnqueueResult> EnqueueAsync(int repositoryId, int specIssueNumber, CancellationToken cancellationToken)
    {
        Calls.Add((repositoryId, specIssueNumber));
        return Task.FromResult(Result);
    }
}

internal sealed class FakeRunQueries : IRunQueries
{
    public List<SpecRunView> SpecRuns { get; } = [];

    public List<TicketRunView> Tickets { get; } = [];

    public List<StepRunView> Steps { get; } = [];

    public List<RunEventView> Events { get; } = [];

    public List<StackLayerView> Stack { get; } = [];

    public Task<IReadOnlyList<SpecRunView>> ListSpecRunsAsync(int repositoryId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<SpecRunView>>(SpecRuns.Where(run => run.RepositoryId == repositoryId).ToArray());

    public Task<SpecRunView?> GetSpecRunAsync(RunId specRunId, CancellationToken cancellationToken) =>
        Task.FromResult(SpecRuns.FirstOrDefault(run => run.Id == specRunId.Value));

    public Task<IReadOnlyList<TicketRunView>> ListTicketRunsAsync(RunId specRunId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<TicketRunView>>(Tickets.Where(ticket => ticket.SpecRunId == specRunId.Value).ToArray());

    public Task<TicketRunView?> GetTicketRunAsync(TicketRunId ticketRunId, CancellationToken cancellationToken) =>
        Task.FromResult(Tickets.FirstOrDefault(ticket => ticket.Id == ticketRunId.Value));

    public Task<IReadOnlyList<StepRunView>> ListStepsAsync(TicketRunId ticketRunId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<StepRunView>>(Steps.Where(step => step.TicketRunId == ticketRunId.Value).ToArray());

    public Task<StepRunView?> GetStepAsync(StepRunId stepRunId, CancellationToken cancellationToken) =>
        Task.FromResult(Steps.FirstOrDefault(step => step.Id == stepRunId.Value));

    public Task<IReadOnlyList<RunEventView>> ListEventsAsync(RunId specRunId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<RunEventView>>(Events.Where(runEvent => runEvent.SpecRunId == specRunId.Value).ToArray());

    public Task<IReadOnlyList<StackLayerView>> ListStackAsync(RunId specRunId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<StackLayerView>>(Stack.ToArray());
}

internal sealed class FakeAgentLogReader : IAgentLogReader
{
    public List<AgentLogView> Entries { get; } = [];

    public int? LastAfterSequence { get; private set; }

    public Task<IReadOnlyList<AgentLogView>> ReadAsync(StepRunId stepRunId, int afterSequence, CancellationToken cancellationToken)
    {
        LastAfterSequence = afterSequence;
        return Task.FromResult<IReadOnlyList<AgentLogView>>(Entries.Where(entry => entry.Sequence > afterSequence).ToArray());
    }
}

internal sealed class FakeClock : IClock
{
    public DateTimeOffset UtcNow => ApiData.Now;
}

internal sealed class FakePrerequisiteValidator : IPrerequisiteValidator
{
    public PrerequisiteCheck[] Checks { get; set; } = [new("Git", PrerequisiteStatus.Passed, "git 2.50")];

    public Task<PrerequisiteReport> ValidateAsync(CancellationToken cancellationToken) => Task.FromResult(new PrerequisiteReport(Checks));
}
