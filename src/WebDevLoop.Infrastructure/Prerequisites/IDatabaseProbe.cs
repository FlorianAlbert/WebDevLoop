namespace WebDevLoop.Infrastructure.Prerequisites;

public sealed record DatabaseState(bool CanConnect, IReadOnlyList<string> PendingMigrations);

public interface IDatabaseProbe
{
    Task<DatabaseState> InspectAsync(CancellationToken cancellationToken);
}
