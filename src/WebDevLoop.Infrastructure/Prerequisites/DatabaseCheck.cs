using WebDevLoop.Core.Ports;

namespace WebDevLoop.Infrastructure.Prerequisites;

/// <summary>Run after startup migrations: a database that is unreachable or still has pending migrations is a failure.</summary>
public sealed class DatabaseCheck(IDatabaseProbe database) : IPrerequisiteCheck
{
    public const string CheckName = "SQLite database";

    public string Name => CheckName;

    public async Task<PrerequisiteCheck> RunAsync(CancellationToken cancellationToken)
    {
        DatabaseState state = await database.InspectAsync(cancellationToken);
        if (!state.CanConnect)
        {
            return CheckResult.Failed(Name, "The SQLite database cannot be opened.", "Check the database path and file permissions.");
        }

        return state.PendingMigrations.Count > 0
            ? CheckResult.Failed(
                Name,
                $"Pending migrations: {string.Join(", ", state.PendingMigrations)}.",
                "Apply the database migrations before starting workflows.")
            : CheckResult.Passed(Name, "The SQLite database is reachable and fully migrated.");
    }
}
