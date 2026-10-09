using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Ports;

namespace WebDevLoop.Infrastructure.GitHub.Stacks;

internal sealed record StackPullRequestDto(int Number);

/// <summary>GitHub lists <c>pull_requests</c> from the bottom of the stack to the top.</summary>
internal sealed record StackDto(int Number, StackPullRequestDto[] PullRequests)
{
    public PullStackSnapshot ToSnapshot() => new(Number, PullRequests.Select(pull => new PullRequestNumber(pull.Number)).ToArray());
}
