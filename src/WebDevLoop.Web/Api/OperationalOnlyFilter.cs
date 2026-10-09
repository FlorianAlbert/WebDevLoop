using WebDevLoop.Infrastructure.Prerequisites;

namespace WebDevLoop.Web.Api;

/// <summary>Rejects mutating workflow endpoints with 503 and the failing prerequisites while the host is in diagnostic-only mode.</summary>
public sealed class OperationalOnlyFilter : IEndpointFilter
{
    public ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        ReadinessSnapshot snapshot = context.HttpContext.RequestServices.GetRequiredService<DiagnosticReadiness>().Current;
        return snapshot.Mode == ReadinessMode.Operational
            ? next(context)
            : ValueTask.FromResult<object?>(ApiProblems.PrerequisitesFailed(snapshot));
    }
}
