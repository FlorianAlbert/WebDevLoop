using WebDevLoop.Core.Domain;

namespace WebDevLoop.Core.Ports;

/// <param name="ExpectedRemoteTip">Lease: the remote ref must currently be here; null means it must not exist yet (immutable stack refs).</param>
public sealed record RefPush(BranchName Branch, CommitSha Commit, CommitSha? ExpectedRemoteTip);
