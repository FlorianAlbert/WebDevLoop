using WebDevLoop.Core.Orchestration.SpecQueue;

namespace WebDevLoop.Web.Api.Contracts;

/// <summary>A batch item's outcome, actionable message, and new or existing run id when available.</summary>
public sealed record QueueSpecItemResult(int SpecIssueNumber, EnqueueOutcome Outcome, string? RunId, string Message);
