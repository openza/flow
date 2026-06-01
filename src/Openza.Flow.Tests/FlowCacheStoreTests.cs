using Openza.Flow.Core.Models;
using Openza.Flow.Core.Services;
using Xunit;

namespace Openza.Flow.Tests;

public sealed class FlowCacheStoreTests
{
    [Fact]
    public async Task CacheRoundTripsJson()
    {
        var directory = Path.Combine(Path.GetTempPath(), "openza-flow-tests", Guid.NewGuid().ToString("N"));
        var store = new FileFlowCacheStore(directory);
        var pullRequests = new[]
        {
            new PullRequest(
                1,
                10,
                "Test",
                "",
                "open",
                "https://github.com/openza/flow/pull/10",
                DateTimeOffset.UtcNow,
                DateTimeOffset.UtcNow,
                false,
                new GithubUser(0, "octocat", "", ""),
                new GithubRepository("openza/flow", "https://github.com/openza/flow"),
                [],
                "main",
                "feature")
        };

        await store.SetAsync("review_requests", pullRequests);
        var cached = await store.GetAsync<List<PullRequest>>("review_requests");

        Assert.NotNull(cached);
        Assert.Equal("Test", Assert.Single(cached).Title);
    }

    [Fact]
    public async Task MalformedJsonReturnsNull()
    {
        var directory = Path.Combine(Path.GetTempPath(), "openza-flow-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(Path.Combine(directory, "bad.json"), "{not json");
        var store = new FileFlowCacheStore(directory);

        var cached = await store.GetAsync<List<PullRequest>>("bad");

        Assert.Null(cached);
    }

    [Fact]
    public async Task CacheWriteFailureDoesNotThrow()
    {
        var filePath = Path.Combine(Path.GetTempPath(), "openza-flow-tests", $"{Guid.NewGuid():N}.cache");
        Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
        await File.WriteAllTextAsync(filePath, "not a directory");
        var store = new FileFlowCacheStore(filePath);

        await store.SetAsync("review_requests", Array.Empty<PullRequest>());
        await store.ClearAsync();
    }

    [Fact]
    public async Task CacheReadAccessFailureReturnsNull()
    {
        var directory = Path.Combine(Path.GetTempPath(), "openza-flow-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(directory, "review_requests.json"));
        var store = new FileFlowCacheStore(directory);

        var cached = await store.GetAsync<List<PullRequest>>("review_requests");

        Assert.Null(cached);
    }
}
