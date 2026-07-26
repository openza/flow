using System.Net;
using System.Text.Json;
using Openza.Flow.Core.Models;
using Openza.Flow.Core.Services;
using Xunit;

namespace Openza.Flow.Tests;

public sealed class GitHubRepositoryActivityServiceTests
{
    [Fact]
    public void MapsRepositoryReleaseAndWorkflowRunRestJson()
    {
        using var repos = JsonDocument.Parse("""
            [
              {
                "name": "flow",
                "full_name": "openza/flow",
                "html_url": "https://github.com/openza/flow",
                "default_branch": "main",
                "pushed_at": "2026-06-01T01:00:00Z",
                "owner": { "login": "openza" }
              }
            ]
            """);
        var repo = Assert.Single(GitHubResponseMapper.MapRepositorySummaries(repos.RootElement));

        using var releases = JsonDocument.Parse("""
            [
              {
                "id": 100,
                "name": "",
                "tag_name": "v1.0.0",
                "html_url": "https://github.com/openza/flow/releases/tag/v1.0.0",
                "draft": true,
                "prerelease": false,
                "created_at": "2026-06-01T02:00:00Z",
                "published_at": null,
                "author": { "login": "octocat" }
              }
            ]
            """);
        var release = Assert.Single(GitHubResponseMapper.MapReleases(releases.RootElement, repo));

        Assert.Equal("openza/flow", repo.FullName);
        Assert.Equal("v1.0.0", release.Name);
        Assert.True(release.Draft);
        Assert.Null(release.PublishedAt);
        Assert.Equal(DateTimeOffset.Parse("2026-06-01T02:00:00Z"), release.SortTimestamp);

        using var runs = JsonDocument.Parse("""
            {
              "workflow_runs": [
                {
                  "id": 200,
                  "name": "CI",
                  "display_title": "Build main",
                  "status": "completed",
                  "conclusion": "success",
                  "event": "push",
                  "head_branch": "main",
                  "head_sha": "abc123",
                  "run_number": 12,
                  "html_url": "https://github.com/openza/flow/actions/runs/200",
                  "created_at": "2026-06-01T03:00:00Z",
                  "updated_at": "2026-06-01T03:05:00Z",
                  "head_commit": { "message": "Build the app" }
                }
              ]
            }
            """);
        var run = Assert.Single(GitHubResponseMapper.MapWorkflowRuns(runs.RootElement, repo));

        Assert.Equal("CI", run.WorkflowName);
        Assert.Equal("Build main", run.DisplayTitle);
        Assert.Equal("success", run.Conclusion);
        Assert.Equal("Build the app", run.CommitTitle);
    }

    [Fact]
    public async Task ReleasesAreSortedCappedAndSkipPerRepositoryFailures()
    {
        var tokenStore = new InMemoryTokenStore();
        await tokenStore.SaveTokenAsync("token");
        var service = new GitHubRepositoryActivityService(
            new HttpClient(new StubHandler(ReleaseResponse)),
            tokenStore);

        var result = await service.GetRecentReleasesAsync("openza");

        Assert.Equal(50, result.Items.Count);
        Assert.Equal(50, result.ScannedRepositoryCount);
        Assert.Equal(1, result.SkippedRepositoryCount);
        Assert.Single(result.Warnings);
        Assert.Equal("v50-2", result.Items[0].TagName);
    }

    [Fact]
    public async Task WorkflowRunsAreSortedCappedAndSkipPerRepositoryFailures()
    {
        var tokenStore = new InMemoryTokenStore();
        await tokenStore.SaveTokenAsync("token");
        var service = new GitHubRepositoryActivityService(
            new HttpClient(new StubHandler(WorkflowRunResponse)),
            tokenStore);

        var result = await service.GetRecentWorkflowRunsAsync("openza");

        Assert.Equal(50, result.Items.Count);
        Assert.Equal(50, result.ScannedRepositoryCount);
        Assert.Equal(1, result.SkippedRepositoryCount);
        Assert.Single(result.Warnings);
        Assert.Equal(502, result.Items[0].RunNumber);
    }

    [Fact]
    public async Task AllOrganizationsUsesAuthenticatedOrganizationRepositories()
    {
        var tokenStore = new InMemoryTokenStore();
        await tokenStore.SaveTokenAsync("token");
        Uri? repositoryRequest = null;
        var service = new GitHubRepositoryActivityService(
            new HttpClient(new StubHandler(request =>
            {
                if (request.RequestUri?.AbsolutePath == "/user/repos")
                {
                    repositoryRequest = request.RequestUri;
                    return Json("[]");
                }

                return new HttpResponseMessage(HttpStatusCode.NotFound);
            })),
            tokenStore);

        var result = await service.GetRecentReleasesAsync(null);

        Assert.Empty(result.Items);
        Assert.NotNull(repositoryRequest);
        Assert.Contains("affiliation=organization_member", repositoryRequest.Query);
        Assert.Contains("visibility=all", repositoryRequest.Query);
    }

    private static HttpResponseMessage ReleaseResponse(HttpRequestMessage request)
    {
        var path = request.RequestUri?.AbsolutePath ?? string.Empty;
        if (path == "/orgs/openza/repos")
        {
            return Json(RepositoriesJson());
        }

        if (path.Contains("/repo-2/", StringComparison.OrdinalIgnoreCase))
        {
            return new HttpResponseMessage(HttpStatusCode.Forbidden);
        }

        var index = RepositoryIndex(path);
        var firstCreatedAt = Timestamp(index, item: 1);
        var firstPublishedAt = Timestamp(index, item: 1, minutesOffset: 30);
        var secondCreatedAt = Timestamp(index, item: 2);
        var secondPublishedAt = Timestamp(index, item: 2, minutesOffset: 30);
        return Json($$"""
            [
              {
                "id": {{index}}1,
                "name": "Release {{index}}.1",
                "tag_name": "v{{index}}-1",
                "html_url": "https://github.com/openza/repo-{{index}}/releases/tag/v{{index}}-1",
                "draft": false,
                "prerelease": false,
                "created_at": "{{firstCreatedAt}}",
                "published_at": "{{firstPublishedAt}}",
                "author": { "login": "octocat" }
              },
              {
                "id": {{index}}2,
                "name": "Release {{index}}.2",
                "tag_name": "v{{index}}-2",
                "html_url": "https://github.com/openza/repo-{{index}}/releases/tag/v{{index}}-2",
                "draft": false,
                "prerelease": false,
                "created_at": "{{secondCreatedAt}}",
                "published_at": "{{secondPublishedAt}}",
                "author": { "login": "octocat" }
              }
            ]
            """);
    }

    private static HttpResponseMessage WorkflowRunResponse(HttpRequestMessage request)
    {
        var path = request.RequestUri?.AbsolutePath ?? string.Empty;
        if (path == "/orgs/openza/repos")
        {
            return Json(RepositoriesJson());
        }

        if (path.Contains("/repo-2/", StringComparison.OrdinalIgnoreCase))
        {
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }

        var index = RepositoryIndex(path);
        var firstCreatedAt = Timestamp(index, item: 1);
        var firstUpdatedAt = Timestamp(index, item: 1, minutesOffset: 5);
        var secondCreatedAt = Timestamp(index, item: 2);
        var secondUpdatedAt = Timestamp(index, item: 2, minutesOffset: 5);
        return Json($$"""
            {
              "workflow_runs": [
                {
                  "id": {{index}}1,
                  "name": "CI",
                  "display_title": "Build repo {{index}}.1",
                  "status": "completed",
                  "conclusion": "success",
                  "event": "push",
                  "head_branch": "main",
                  "head_sha": "sha{{index}}1",
                  "run_number": {{index}}1,
                  "html_url": "https://github.com/openza/repo-{{index}}/actions/runs/{{index}}1",
                  "created_at": "{{firstCreatedAt}}",
                  "updated_at": "{{firstUpdatedAt}}",
                  "head_commit": { "message": "Commit {{index}}.1" }
                },
                {
                  "id": {{index}}2,
                  "name": "CI",
                  "display_title": "Build repo {{index}}.2",
                  "status": "completed",
                  "conclusion": "success",
                  "event": "push",
                  "head_branch": "main",
                  "head_sha": "sha{{index}}2",
                  "run_number": {{index}}2,
                  "html_url": "https://github.com/openza/repo-{{index}}/actions/runs/{{index}}2",
                  "created_at": "{{secondCreatedAt}}",
                  "updated_at": "{{secondUpdatedAt}}",
                  "head_commit": { "message": "Commit {{index}}.2" }
                }
              ]
            }
            """);
    }

    private static string RepositoriesJson()
    {
        var repositories = Enumerable.Range(1, 52).Select(index => $$"""
            {
              "name": "repo-{{index}}",
              "full_name": "openza/repo-{{index}}",
              "html_url": "https://github.com/openza/repo-{{index}}",
              "default_branch": "main",
              "pushed_at": "2026-06-01T00:00:00Z",
              "owner": { "login": "openza" }
            }
            """);
        return $"[{string.Join(",", repositories)}]";
    }

    private static int RepositoryIndex(string path)
    {
        var marker = "/repo-";
        var start = path.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        Assert.True(start >= 0, $"Unexpected path: {path}");
        start += marker.Length;
        var end = path.IndexOf('/', start);
        return int.Parse(path[start..end]);
    }

    private static string Timestamp(int index, int item = 0, int minutesOffset = 0)
    {
        return DateTimeOffset.Parse("2026-06-01T00:00:00Z")
            .AddMinutes((index * 10) + item + minutesOffset)
            .ToString("O");
    }

    private static HttpResponseMessage Json(string body)
    {
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(body)
        };
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responseFactory;

        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory)
        {
            _responseFactory = responseFactory;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(_responseFactory(request));
        }
    }
}
