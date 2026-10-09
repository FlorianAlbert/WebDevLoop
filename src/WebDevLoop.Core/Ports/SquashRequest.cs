using WebDevLoop.Core.Domain;

namespace WebDevLoop.Core.Ports;

/// <summary>
/// Squash the tree of <paramref name="Source"/> onto <paramref name="IntegrationTip"/> as a single commit whose only parent is
/// <paramref name="IntegrationTip"/>. No ref is moved; use <see cref="IGitWorkspace.UpdateBranchAsync"/> afterwards.
/// </summary>
public sealed record SquashRequest(CommitSha Source, CommitSha IntegrationTip, string Message);
