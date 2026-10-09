using WebDevLoop.Core.Management;
using WebDevLoop.Core.Queries;
using WebDevLoop.Core.Settings;

namespace WebDevLoop.Web.Tests.Components.Settings;

internal sealed class RecordingSettingsManager : ISettingsManager
{
    public SettingsProfileData Global { get; set; } = new();

    public SettingsProfileData Repository { get; set; } = new();

    public CommandResult<SettingsProfileData>? SaveGlobalOverride { get; set; }

    public CommandResult<SettingsProfileData>? SaveRepositoryOverride { get; set; }

    public CommandResult<SettingsProfileData>? RepositoryLoadOverride { get; set; }

    public List<SettingsProfileData> SavedGlobal { get; } = [];

    public List<(int Id, SettingsProfileData Data)> SavedRepository { get; } = [];

    public Task<SettingsProfileData> GetGlobalAsync(CancellationToken cancellationToken) => Task.FromResult(Global);

    public Task<CommandResult<SettingsProfileData>> SaveGlobalAsync(SettingsProfileData data, CancellationToken cancellationToken)
    {
        SavedGlobal.Add(data);
        if (SaveGlobalOverride is null)
        {
            Global = data;
        }

        return Task.FromResult(SaveGlobalOverride ?? CommandResult<SettingsProfileData>.Succeeded(data));
    }

    public Task<CommandResult<SettingsProfileData>> GetRepositoryAsync(int repositoryId, CancellationToken cancellationToken) =>
        Task.FromResult(RepositoryLoadOverride ?? CommandResult<SettingsProfileData>.Succeeded(Repository));

    public Task<CommandResult<SettingsProfileData>> SaveRepositoryAsync(int repositoryId, SettingsProfileData data, CancellationToken cancellationToken)
    {
        SavedRepository.Add((repositoryId, data));
        if (SaveRepositoryOverride is null)
        {
            Repository = data;
        }

        return Task.FromResult(SaveRepositoryOverride ?? CommandResult<SettingsProfileData>.Succeeded(data));
    }

    public Task<CommandResult<EffectiveSettingsView>> GetEffectiveAsync(int repositoryId, CancellationToken cancellationToken) =>
        Task.FromResult(CommandResult<EffectiveSettingsView>.NotFound("not used by the settings UI"));
}

internal sealed class StubRepositoryQueries : IRepositoryQueries
{
    private static readonly DateTimeOffset Now = new(2026, 10, 9, 12, 0, 0, TimeSpan.Zero);

    public List<RepositoryView> Repositories { get; } =
    [
        new(1, "acme", "widgets", "main", "https://github.com/acme/widgets.git", "/work/acme/widgets", true, Now, Now),
        new(2, "acme", "gadgets", "main", "https://github.com/acme/gadgets.git", "/work/acme/gadgets", true, Now, Now),
    ];

    public Task<IReadOnlyList<RepositoryView>> ListAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<RepositoryView>>(Repositories.ToArray());

    public Task<RepositoryView?> GetAsync(int repositoryId, CancellationToken cancellationToken) =>
        Task.FromResult(Repositories.FirstOrDefault(repository => repository.Id == repositoryId));
}
