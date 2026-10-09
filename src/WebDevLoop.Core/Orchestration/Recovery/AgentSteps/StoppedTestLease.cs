using WebDevLoop.Core.Domain;

namespace WebDevLoop.Core.Orchestration.Recovery.AgentSteps;

/// <param name="Expired">The lease outlived its time-to-live (otherwise it was left behind by a previous process).</param>
public sealed record StoppedTestLease(RunId SpecRunId, int Port, bool Expired, int KilledProcesses);
