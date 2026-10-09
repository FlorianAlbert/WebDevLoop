using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Management;
using WebDevLoop.Core.Orchestration.Recovery.AgentSteps;
using WebDevLoop.Core.Ports;
using WebDevLoop.Core.Settings;
using WebDevLoop.Infrastructure.Persistence;
using WebDevLoop.Infrastructure.Prerequisites;
using WebDevLoop.Web.DependencyInjection;

namespace WebDevLoop.Web.Background;

/// <summary>Prepares the app before the host starts serving and the hosted workers run.</summary>
public interface IAppInitializer
{
    Task InitializeAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Startup sequence: pin the process boot time, create the data directory, apply migrations, seed the global settings on
/// first start, resolve the global settings the process-wide adapters are built from (<see cref="StartupSettings"/>), and
/// evaluate the prerequisites.
/// Nothing here throws for an unhealthy environment: a database that cannot be migrated or a failing prerequisite leaves the
/// app in diagnostic-only mode (UI, health and API up; no workflow) and is logged with its remediation.
/// </summary>
/// <remarks>The readiness is resolved only after the settings, since the prerequisite checks are built from them.</remarks>
public sealed partial class AppInitializer(
    IServiceProvider services,
    IServiceScopeFactory scopes,
    WebDevLoopOptions options,
    StartupSettings startupSettings,
    EffectiveSettings embeddedDefaults,
    SettingsResolver resolver,
    ILogger<AppInitializer> logger) : IAppInitializer
{
    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        // Pinned now: recovery treats steps started before the boot time as left over by a previous process.
        services.GetRequiredService<ProcessBoot>();
        startupSettings.Resolve(await PrepareDatabaseAsync(cancellationToken) ?? embeddedDefaults);
        ReadinessSnapshot snapshot = await services.GetRequiredService<DiagnosticReadiness>().RefreshAsync(cancellationToken);
        if (snapshot.Mode == ReadinessMode.Operational)
        {
            LogOperational(logger, options.ResolvedDataDirectory);
            return;
        }

        foreach (PrerequisiteCheck failed in snapshot.FailedChecks)
        {
            LogFailedPrerequisite(logger, failed.Name, failed.Message, failed.Remediation ?? "-");
        }

        LogDiagnosticOnly(logger, snapshot.FailedChecks.Count);
    }

    /// <returns>The resolved global settings, or null when the database is unusable.</returns>
    private async Task<EffectiveSettings?> PrepareDatabaseAsync(CancellationToken cancellationToken)
    {
        try
        {
            Directory.CreateDirectory(options.ResolvedDataDirectory);
            Directory.CreateDirectory(Path.GetDirectoryName(options.ResolvedDatabasePath)!);
            await using AsyncServiceScope scope = scopes.CreateAsyncScope();
            await PersistenceDatabase.MigrateAsync(scope.ServiceProvider.GetRequiredService<WebDevLoopDbContext>(), cancellationToken);
            if (await scope.ServiceProvider.GetRequiredService<GlobalSettingsSeeder>().SeedAsync(cancellationToken))
            {
                LogSeeded(logger);
            }

            SettingsProfile? global = await scope.ServiceProvider.GetRequiredService<ISettingsProfileRepository>().GetGlobalAsync(cancellationToken);
            return global is null ? null : resolver.Resolve(global);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            LogDatabaseUnavailable(logger, exception, options.ResolvedDatabasePath);
            return null;
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Prerequisites are met (data directory {DataDirectory}); the workflow starts after startup recovery.")]
    private static partial void LogOperational(ILogger logger, string dataDirectory);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Prerequisite '{Check}' failed: {Message} Remediation: {Remediation}")]
    private static partial void LogFailedPrerequisite(ILogger logger, string check, string message, string remediation);

    [LoggerMessage(Level = LogLevel.Warning, Message = "{Count} prerequisite(s) failed: running diagnostic-only (UI, health and API are available, the workflow is not). Fix them and use Re-check on the Health page.")]
    private static partial void LogDiagnosticOnly(ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Information, Message = "First start: seeded the global settings with the embedded defaults and prompt templates.")]
    private static partial void LogSeeded(ILogger logger);

    [LoggerMessage(Level = LogLevel.Error, Message = "The database at {DatabasePath} could not be migrated; running diagnostic-only with the embedded default settings.")]
    private static partial void LogDatabaseUnavailable(ILogger logger, Exception exception, string databasePath);
}
