using WebDevLoop.Core.Ports;
using WebDevLoop.Infrastructure.Prerequisites;

namespace WebDevLoop.Web.Api.Contracts;

public sealed record PrerequisiteCheckResponse(string Name, PrerequisiteStatus Status, string Message, string? Remediation);

public sealed record PrerequisitesResponse(ReadinessMode Mode, DateTimeOffset? EvaluatedAt, IReadOnlyList<PrerequisiteCheckResponse> Checks);

/// <summary><see cref="Status"/> is <c>Healthy</c> only in operational mode; otherwise <c>Unhealthy</c> with the failed checks.</summary>
public sealed record HealthResponse(string Status, ReadinessMode Mode, DateTimeOffset? EvaluatedAt, IReadOnlyList<PrerequisiteCheckResponse> FailedChecks);
