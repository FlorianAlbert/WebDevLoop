using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using WebDevLoop.Core.Ports;
using WebDevLoop.Infrastructure.Persistence;
using WebDevLoop.Infrastructure.Skills;

namespace WebDevLoop.Infrastructure.Prerequisites;

public static class PrerequisiteServiceCollectionExtensions
{
    /// <summary>
    /// Registers the startup checks, the <see cref="IPrerequisiteValidator"/> and the shared <see cref="DiagnosticReadiness"/>.
    /// Requires an <see cref="IClock"/>, <see cref="IModelCatalog"/> and scoped settings repositories and resolver.
    /// Running them never throws for a failed prerequisite, so the web host can
    /// start in diagnostic-only mode; call <see cref="DiagnosticReadiness.RefreshAsync"/> after migrations have been applied.
    /// </summary>
    public static IServiceCollection AddPrerequisites(
        this IServiceCollection services,
        PrerequisiteOptions options,
        string databaseConnectionString,
        BundledSkillsCatalog skills) =>
        services.AddPrerequisites(_ => options, databaseConnectionString, skills);

    /// <summary>
    /// Like <see cref="AddPrerequisites(IServiceCollection, PrerequisiteOptions, string, BundledSkillsCatalog)"/>, but the
    /// options are resolved from the container when the checks are first needed (e.g. once settings were loaded at startup).
    /// </summary>
    public static IServiceCollection AddPrerequisites(
        this IServiceCollection services,
        Func<IServiceProvider, PrerequisiteOptions> options,
        string databaseConnectionString,
        BundledSkillsCatalog skills)
    {
        var fileSystem = new FileSystemProbe();
        var database = new EfDatabaseProbe(() => new WebDevLoopDbContext(
            new DbContextOptionsBuilder<WebDevLoopDbContext>().UseSqlite(databaseConnectionString).Options));
        services.AddSingleton(new PrerequisiteOptionsSource(options));
        services.AddScoped<AgentModelSettingsReader>();
        services.AddSingleton(provider => new AgentModelsCheck(
            provider.GetRequiredService<IModelCatalog>(),
            async cancellationToken =>
            {
                await using AsyncServiceScope scope = provider.CreateAsyncScope();
                return await scope.ServiceProvider.GetRequiredService<AgentModelSettingsReader>().ReadAsync(cancellationToken);
            }));

        Func<PrerequisiteOptions, IProcessProbe, IServiceProvider, IPrerequisiteCheck>[] checks =
        [
            (resolved, _, _) => new WorkspaceRootCheck(resolved, fileSystem),
            (_, _, _) => new DatabaseCheck(database),
            (_, _, _) => new BundledSkillsCheck(skills),
            (resolved, _, _) => new GitHubAuthCheck(resolved),
            (_, _, provider) => provider.GetRequiredService<AgentModelsCheck>(),
            (_, _, _) => new LibGit2SharpCheck(new LibGit2NativeProbe()),
            (resolved, processes, _) => new GitCliCheck(resolved, processes),
            (resolved, processes, _) => new GhStackCheck(resolved, processes),
            (resolved, processes, _) => new CopilotRuntimeCheck(resolved, fileSystem, processes),
            (resolved, processes, _) => new PlaywrightCliCheck(resolved, processes),
            (resolved, _, _) => new TestPortRangeCheck(resolved),
        ];
        foreach (Func<PrerequisiteOptions, IProcessProbe, IServiceProvider, IPrerequisiteCheck> check in checks)
        {
            services.AddSingleton(provider =>
            {
                PrerequisiteOptions resolved = provider.GetRequiredService<PrerequisiteOptionsSource>().Get(provider);
                return check(resolved, new ProcessProbe(resolved.ProbeTimeout), provider);
            });
        }

        services.AddSingleton<IPrerequisiteValidator>(provider => new PrerequisiteValidator(provider.GetServices<IPrerequisiteCheck>()));
        services.AddSingleton<DiagnosticReadiness>();
        return services;
    }

    /// <summary>Resolves the options once per container, so every check sees the same values.</summary>
    private sealed class PrerequisiteOptionsSource(Func<IServiceProvider, PrerequisiteOptions> factory)
    {
        private PrerequisiteOptions? _options;

        public PrerequisiteOptions Get(IServiceProvider provider) =>
            LazyInitializer.EnsureInitialized(ref _options, () => factory(provider));
    }
}
