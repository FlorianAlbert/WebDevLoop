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
        EffectiveSettings? persisted = await PrepareDatabaseAsync(cancellationToken);
        startupSettings.Resolve(persisted ?? embeddedDefaults);
        if (persisted is not null)
        {
            await WarnAboutClonesOutsideWorkspaceRootAsync(persisted.WorkspaceRootDirectory, cancellationToken);
        }

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

    /// <remarks>
    /// Clone paths are stored when a repository is registered, so a changed workspace root does not move them: the git workspace
    /// refuses paths outside the new root and the repository's tickets need attention until the clone is moved (or the root is restored).
    /// </remarks>
    private async Task WarnAboutClonesOutsideWorkspaceRootAsync(string workspaceRoot, CancellationToken cancellationToken)
    {
        try
        {
            await using AsyncServiceScope scope = scopes.CreateAsyncScope();
            IReadOnlyList<RepositoryRecord> repositories = await scope.ServiceProvider.GetRequiredService<IRepositoryRecordRepository>().ListAsync(cancellationToken);
            foreach (RepositoryRecord repository in WorkspaceRootDrift.FindOutside(workspaceRoot, repositories))
            {
                LogCloneOutsideWorkspaceRoot(logger, repository.Ref.ToString(), repository.LocalPath, workspaceRoot);
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            LogWorkspaceRootCheckFailed(logger, exception);
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

    [LoggerMessage(Level = LogLevel.Warning, Message = "Repository {Repository} is cloned at {LocalPath}, outside the workspace root {WorkspaceRoot} the app started with: its worktrees cannot be created and its tickets will need attention. Move the clone under the workspace root and update the repository's local path, or restore the previous workspace root and restart.")]
    private static partial void LogCloneOutsideWorkspaceRoot(ILogger logger, string repository, string localPath, string workspaceRoot);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The clone locations could not be checked against the workspace root.")]
    private static partial void LogWorkspaceRootCheckFailed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "The database at {DatabasePath} could not be migrated; running diagnostic-only with the embedded default settings.")]
    private static partial void LogDatabaseUnavailable(ILogger logger, Exception exception, string databasePath);
}
