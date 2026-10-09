using WebDevLoop.Core.Domain;

namespace WebDevLoop.Core.Ports;

public sealed record PullStackSnapshot(int StackNumber, IReadOnlyList<PullRequestNumber> BottomToTop);
