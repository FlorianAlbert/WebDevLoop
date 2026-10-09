using System.Net;
using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Ports;
using WebDevLoop.Infrastructure.GitHub.Pulls;

namespace WebDevLoop.Infrastructure.GitHub.Stacks;

internal sealed class StacksClient(GitHubApiClient api, IGhCommandRunner? ghRunner)
{
    private const int MinimumCreateSize = 2;

    private readonly GhStackLinker? _linker = ghRunner is null ? null : new GhStackLinker(ghRunner);

    public async Task<PullStackSnapshot?> FindAsync(GitHubRepoRef repo, PullRequestNumber member, CancellationToken cancellationToken)
    {
        GitHubApiResponse response = await api.SendAsync(HttpMethod.Get, repo, $"stacks?pull_request={member}", null, cancellationToken);
        if (response.Status == HttpStatusCode.NotFound)
        {
            return null;
        }

        return response.EnsureSuccess().Read<StackDto[]>()
            .Select(stack => stack.ToSnapshot())
            .FirstOrDefault(stack => stack.BottomToTop.Contains(member));
    }

    public async Task<PullStackSnapshot> CreateAsync(GitHubRepoRef repo, IReadOnlyList<PullRequestNumber> bottomToTop, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(bottomToTop);
        if (bottomToTop.Count < MinimumCreateSize)
        {
            throw new ArgumentException($"A stack needs at least {MinimumCreateSize} pull requests.", nameof(bottomToTop));
        }

        int[] numbers = bottomToTop.Select(number => number.Value).ToArray();
        GitHubApiResponse response = await api.SendAsync(HttpMethod.Post, repo, "stacks", new { pull_requests = numbers }, cancellationToken);
        if (response.IsSuccess)
        {
            return response.Read<StackDto>().ToSnapshot();
        }

        if (response.Status == HttpStatusCode.UnprocessableEntity
            && await FindAsync(repo, bottomToTop[0], cancellationToken) is { } existing
            && existing.BottomToTop.SequenceEqual(bottomToTop))
        {
            return existing;
        }

        if (response.Status == HttpStatusCode.NotFound && _linker is not null)
        {
            await LinkAsync(repo, numbers, cancellationToken);
            return await FindAsync(repo, bottomToTop[0], cancellationToken)
                ?? throw new GhStackFallbackException("'gh stack link' succeeded but the stack cannot be found afterwards.");
        }

        throw response.ToException();
    }

    public async Task<PullStackSnapshot> AddAsync(GitHubRepoRef repo, int stackNumber, PullRequestNumber pullRequest, CancellationToken cancellationToken)
    {
        GitHubApiResponse response = await api.SendAsync(
            HttpMethod.Post, repo, $"stacks/{stackNumber}/add", new { pull_requests = new[] { pullRequest.Value } }, cancellationToken);
        if (response.IsSuccess)
        {
            return response.Read<StackDto>().ToSnapshot();
        }

        bool linkable = response.Status == HttpStatusCode.NotFound && _linker is not null;
        if (linkable)
        {
            await LinkAsync(repo, [stackNumber, pullRequest.Value], cancellationToken);
        }

        // 409/422 usually mean a retry of an add that already happened; accept it only if the PR really is the top layer.
        if (linkable || response.Status is HttpStatusCode.Conflict or HttpStatusCode.UnprocessableEntity)
        {
            PullStackSnapshot current = await GetAsync(repo, stackNumber, cancellationToken);
            if (current.BottomToTop is [.., var top] && top == pullRequest)
            {
                return current;
            }
        }

        throw response.ToException();
    }

    private async Task<PullStackSnapshot> GetAsync(GitHubRepoRef repo, int stackNumber, CancellationToken cancellationToken) =>
        (await api.SendAsync(HttpMethod.Get, repo, $"stacks/{stackNumber}", null, cancellationToken)).EnsureSuccess().Read<StackDto>().ToSnapshot();

    private async Task LinkAsync(GitHubRepoRef repo, IReadOnlyList<int> targets, CancellationToken cancellationToken)
    {
        GitHubAccessToken token = await api.AcquireTokenAsync(repo, cancellationToken);
        await _linker!.LinkAsync(repo, token.Value, targets, cancellationToken);
    }
}
