namespace WebDevLoop.Web.Api.Contracts;

/// <summary>Parent specs in the exact order to enqueue. Each item is checked independently at submission.</summary>
public sealed record QueueSpecsRequest(IReadOnlyList<int>? SpecIssueNumbers);
