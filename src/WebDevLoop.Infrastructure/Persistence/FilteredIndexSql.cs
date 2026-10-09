using WebDevLoop.Core.Domain;

namespace WebDevLoop.Infrastructure.Persistence;

/// <summary>SQL predicates for partial unique indexes, built from the domain's status rules so they cannot drift from the state machine.</summary>
internal static class FilteredIndexSql
{
    public static string ActiveSpecRun { get; } = In("Status", StatusSets.Active<SpecRunStatus>(SpecRunStatusRules.IsActive));

    public static string ActiveStep { get; } = In("Status", StatusSets.Active<StepStatus>(StepStatusRules.IsActive));

    public static string ImplementOrFixStep { get; } = In("Kind", Enum.GetValues<StepKind>().Where(StepKindRules.IsImplementOrFix));

    public static string IncompleteSaga { get; } = $"\"Checkpoint\" <> '{IntegrationSagaCheckpoint.Completed}'";

    public static string UnreleasedLease => "\"ReleasedAt\" IS NULL";

    private static string In<T>(string column, IEnumerable<T> values) where T : struct, Enum =>
        $"\"{column}\" IN ({string.Join(", ", values.Select(value => $"'{value}'"))})";
}
