using WebDevLoop.Core.Domain;

namespace WebDevLoop.Core.Orchestration.Integration;

/// <summary>
/// A ticket in <see cref="TicketRunStatus.Integrating"/> whose integration saga must run. The repository id is part of the
/// assignment so the runner can enter the per-repository merge lock before it loads any state.
/// </summary>
public sealed record IntegrationAssignment(int RepositoryId, RunId SpecRunId, TicketRunId TicketRunId);
