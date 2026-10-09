using WebDevLoop.Core.Queries;

namespace WebDevLoop.Web.Api.Contracts;

public sealed record QueueSpecRequest(int SpecIssueNumber);

/// <summary><see cref="Outcome"/> is <c>Queued</c> (new run) or <c>AlreadyQueued</c> (the repository already has an unfinished run for that spec issue).</summary>
public sealed record QueueSpecResponse(string RunId, string Outcome);

/// <summary>Entries newer than the requested sequence. Poll again with <see cref="LastSequence"/> to resume.</summary>
public sealed record AgentLogsResponse(string StepRunId, IReadOnlyList<AgentLogView> Entries, int LastSequence);
