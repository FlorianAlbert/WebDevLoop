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
    /// Requires an <see cref="IClock"/>. Resolving and running them never throws for a failed prerequisite, so the web host can
    /// start in diagnostic-only mode; call <see cref="DiagnosticReadiness.RefreshAsync"/> after migrations have been applied.
    /// </summary>
    public static IServiceCollection AddPrerequisites(
        this IServiceCollection services,
        PrerequisiteOptions options,
        string databaseConnectionString,
        BundledSkillsCatalog skills)
    {
        var fileSystem = new FileSystemProbe();
        var processes = new ProcessProbe(options.ProbeTimeout);
        var database = new EfDatabaseProbe(() => new WebDevLoopDbContext(
            new DbContextOptionsBuilder<WebDevLoopDbContext>().UseSqlite(databaseConnectionString).Options));

        IPrerequisiteCheck[] checks =
        [
            new WorkspaceRootCheck(options, fileSystem),
            new DatabaseCheck(database),
            new BundledSkillsCheck(skills),
            new GitHubAuthCheck(options),
            new LibGit2SharpCheck(new LibGit2NativeProbe()),
            new GitCliCheck(options, processes),
            new GhStackCheck(options, processes),
            new CopilotRuntimeCheck(options, fileSystem, processes),
            new PlaywrightCliCheck(options, processes),
            new TestPortRangeCheck(options),
        ];
        foreach (IPrerequisiteCheck check in checks)
        {
            services.AddSingleton(check);
        }

        services.AddSingleton<IPrerequisiteValidator>(provider => new PrerequisiteValidator(provider.GetServices<IPrerequisiteCheck>()));
        services.AddSingleton<DiagnosticReadiness>();
        return services;
    }
}
