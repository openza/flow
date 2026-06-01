using System.Text.Json;
using Openza.Flow.Core.Models;
using Openza.Flow.Core.Services;
using Xunit;

namespace Openza.Flow.Tests;

public sealed class GitHubResponseMapperTests
{
    [Fact]
    public void MapsPullRequestSearchWithLabelsAndPagination()
    {
        using var document = JsonDocument.Parse("""
            {
              "pageInfo": { "hasNextPage": true, "endCursor": "abc" },
              "nodes": [
                {
                  "databaseId": 42,
                  "number": 7,
                  "title": "Native app",
                  "bodyText": "Body",
                  "state": "OPEN",
                  "url": "https://github.com/openza/flow/pull/7",
                  "createdAt": "2026-05-09T10:00:00Z",
                  "updatedAt": "2026-05-09T11:00:00Z",
                  "isDraft": false,
                  "baseRefName": "main",
                  "headRefName": "feature",
                  "author": { "login": "deependra", "avatarUrl": "https://avatar", "url": "https://github.com/deependra" },
                  "repository": { "name": "flow", "owner": { "login": "openza" }, "url": "https://github.com/openza/flow" },
                  "labels": { "nodes": [ { "name": "store", "color": "123456", "description": "Store work" } ] }
                }
              ]
            }
            """);

        var result = GitHubResponseMapper.MapPullRequestSearch(document.RootElement);

        Assert.True(result.HasNextPage);
        Assert.Equal("abc", result.EndCursor);
        var pr = Assert.Single(result.Items);
        Assert.Equal(42, pr.Id);
        Assert.Equal("openza/flow", pr.Repository.FullName);
        Assert.Equal("store", Assert.Single(pr.Labels).Name);
    }

    [Fact]
    public void MapsLatestCurrentUserReviewState()
    {
        using var document = JsonDocument.Parse("""
            {
              "databaseId": 42,
              "number": 7,
              "title": "Native app",
              "url": "https://github.com/openza/flow/pull/7",
              "state": "OPEN",
              "mergedAt": null,
              "updatedAt": "2026-05-09T11:00:00Z",
              "baseRefName": "main",
              "headRefName": "feature",
              "author": { "login": "contributor", "avatarUrl": "", "url": "" },
              "repository": { "name": "flow", "owner": { "login": "openza" }, "url": "https://github.com/openza/flow" },
              "reviews": {
                "nodes": [
                  { "state": "COMMENTED", "author": { "login": "deependra" }, "submittedAt": "2026-05-09T09:00:00Z" },
                  { "state": "APPROVED", "author": { "login": "deependra" }, "submittedAt": "2026-05-09T10:00:00Z" }
                ]
              }
            }
            """);

        var pr = GitHubResponseMapper.MapReviewedPullRequest(document.RootElement, "deependra");

        Assert.Equal(ReviewState.Approved, pr.ReviewState);
        Assert.Equal(MergeState.Open, pr.MergeState);
        Assert.Equal(DateTimeOffset.Parse("2026-05-09T10:00:00Z"), pr.ReviewedAt);
    }
}
