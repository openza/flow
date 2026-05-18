using Openza.Flow.Core.Models;
using Openza.Flow.Core.Services;
using Xunit;

namespace Openza.Flow.Tests;

public sealed class BackgroundRefreshServiceTests
{
    [Fact]
    public async Task FirstRefreshDoesNotNotify()
    {
        var service = new BackgroundRefreshService(_ => Task.FromResult<IReadOnlyList<PullRequest>>([MakePullRequest(1)]));
        var notifications = 0;
        service.NewReviewRequestsFound += (_, _) => notifications++;

        await service.RefreshOnceAsync();

        Assert.Equal(0, notifications);
    }

    [Fact]
    public async Task LaterRefreshNotifiesOnlyNewPullRequests()
    {
        var calls = 0;
        var service = new BackgroundRefreshService(_ =>
        {
            calls++;
            IReadOnlyList<PullRequest> result = calls == 1
                ? [MakePullRequest(1)]
                : [MakePullRequest(2), MakePullRequest(1)];
            return Task.FromResult(result);
        });
        IReadOnlyList<PullRequest>? notified = null;
        service.NewReviewRequestsFound += (_, args) => notified = args.PullRequests;

        await service.RefreshOnceAsync();
        await service.RefreshOnceAsync();

        var pr = Assert.Single(notified!);
        Assert.Equal(2, pr.Id);
    }

    [Fact]
    public async Task LaterRefreshNotifiesWhenBaselineWasEmpty()
    {
        var calls = 0;
        var service = new BackgroundRefreshService(_ =>
        {
            calls++;
            IReadOnlyList<PullRequest> result = calls == 1
                ? []
                : [MakePullRequest(1)];
            return Task.FromResult(result);
        });
        IReadOnlyList<PullRequest>? notified = null;
        service.NewReviewRequestsFound += (_, args) => notified = args.PullRequests;

        await service.RefreshOnceAsync();
        await service.RefreshOnceAsync();

        var pr = Assert.Single(notified!);
        Assert.Equal(1, pr.Id);
    }

    private static PullRequest MakePullRequest(int id)
    {
        return new PullRequest(
            id,
            id,
            $"PR {id}",
            "",
            "open",
            $"https://github.com/openza/flow/pull/{id}",
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            false,
            new GithubUser(0, "octocat", "", ""),
            new GithubRepository("openza/flow", "https://github.com/openza/flow"),
            [],
            "main",
            "feature");
    }
}
