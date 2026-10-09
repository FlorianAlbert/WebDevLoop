using WebDevLoop.Core.Domain;

namespace WebDevLoop.Web.Api;

/// <summary>Route ids that are not valid identifiers cannot exist, so lookups treat them as "not found" instead of failing.</summary>
internal static class ApiIds
{
    public static RunId? SpecRun(string value) => TryCreate(value, id => new RunId(id));

    public static TicketRunId? TicketRun(string value) => TryCreate(value, id => new TicketRunId(id));

    public static StepRunId? Step(string value) => TryCreate(value, id => new StepRunId(id));

    private static T? TryCreate<T>(string value, Func<string, T> create)
        where T : struct
    {
        try
        {
            return create(value);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }
}
