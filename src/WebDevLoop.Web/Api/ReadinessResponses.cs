using WebDevLoop.Core.Ports;
using WebDevLoop.Infrastructure.Prerequisites;
using WebDevLoop.Web.Api.Contracts;

namespace WebDevLoop.Web.Api;

internal static class ReadinessResponses
{
    public static PrerequisiteCheckResponse ToResponse(this PrerequisiteCheck check) => new(check.Name, check.Status, check.Message, check.Remediation);

    public static PrerequisitesResponse ToPrerequisites(this ReadinessSnapshot snapshot) =>
        new(snapshot.Mode, snapshot.EvaluatedAt, snapshot.Report?.Checks.Select(ToResponse).ToArray() ?? []);

    public static HealthResponse ToHealth(this ReadinessSnapshot snapshot) =>
        new(snapshot.Mode == ReadinessMode.Operational ? "Healthy" : "Unhealthy", snapshot.Mode, snapshot.EvaluatedAt, snapshot.FailedChecks.Select(ToResponse).ToArray());
}
