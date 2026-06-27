using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Openza.Flow.Core.Models;

namespace Openza.Flow.Core.Services;

public sealed class GitHubRepositoryActivityService
{
    private const int RepositoryLimit = 50;
    private const int PerRepositoryItemLimit = 3;
    private const int ResultLimit = 50;
    private const int MaxConcurrentRepositoryRequests = 5;

    private readonly HttpClient _httpClient;
    private readonly ITokenStore _tokenStore;

    public GitHubRepositoryActivityService(HttpClient httpClient, ITokenStore tokenStore)
    {
        _httpClient = httpClient;
        _tokenStore = tokenStore;
    }

    public async Task<RepositoryActivityResult<GithubRelease>> GetRecentReleasesAsync(
        string organization,
        CancellationToken cancellationToken = default)
    {
        var repositories = await GetOrganizationRepositoriesAsync(organization, cancellationToken);
        var result = await FetchPerRepositoryAsync(
            repositories,
            repository => GetRepositoryReleasesAsync(repository, cancellationToken),
            cancellationToken);

        var items = result.Items
            .OrderByDescending(release => release.SortTimestamp)
            .Take(ResultLimit)
            .ToList();

        return result with { Items = items };
    }

    public async Task<RepositoryActivityResult<GithubWorkflowRun>> GetRecentWorkflowRunsAsync(
        string organization,
        CancellationToken cancellationToken = default)
    {
        var repositories = await GetOrganizationRepositoriesAsync(organization, cancellationToken);
        var result = await FetchPerRepositoryAsync(
            repositories,
            repository => GetRepositoryWorkflowRunsAsync(repository, cancellationToken),
            cancellationToken);

        var items = result.Items
            .OrderByDescending(run => run.CreatedAt)
            .Take(ResultLimit)
            .ToList();

        return result with { Items = items };
    }

    private async Task<IReadOnlyList<GithubRepositorySummary>> GetOrganizationRepositoriesAsync(
        string organization,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(organization))
        {
            return [];
        }

        var uri = $"{GitHubConstants.ApiBaseUrl}/orgs/{Uri.EscapeDataString(organization.Trim())}/repos?type=all&sort=pushed&direction=desc&per_page={RepositoryLimit}";
        using var response = await SendRestAsync(uri, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"GitHub repository request failed with status {(int)response.StatusCode}.");
        }

        using var document = JsonDocument.Parse(body);
        return GitHubResponseMapper.MapRepositorySummaries(document.RootElement)
            .Take(RepositoryLimit)
            .ToList();
    }

    private async Task<IReadOnlyList<GithubRelease>> GetRepositoryReleasesAsync(
        GithubRepositorySummary repository,
        CancellationToken cancellationToken)
    {
        var uri = $"{GitHubConstants.ApiBaseUrl}/repos/{Uri.EscapeDataString(repository.Owner)}/{Uri.EscapeDataString(repository.Name)}/releases?per_page={PerRepositoryItemLimit}";
        using var response = await SendRestAsync(uri, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw PerRepositoryException(response.StatusCode, repository.FullName, "releases");
        }

        using var document = JsonDocument.Parse(body);
        return GitHubResponseMapper.MapReleases(document.RootElement, repository);
    }

    private async Task<IReadOnlyList<GithubWorkflowRun>> GetRepositoryWorkflowRunsAsync(
        GithubRepositorySummary repository,
        CancellationToken cancellationToken)
    {
        var uri = $"{GitHubConstants.ApiBaseUrl}/repos/{Uri.EscapeDataString(repository.Owner)}/{Uri.EscapeDataString(repository.Name)}/actions/runs?per_page={PerRepositoryItemLimit}";
        using var response = await SendRestAsync(uri, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw PerRepositoryException(response.StatusCode, repository.FullName, "workflow runs");
        }

        using var document = JsonDocument.Parse(body);
        return GitHubResponseMapper.MapWorkflowRuns(document.RootElement, repository);
    }

    private async Task<RepositoryActivityResult<T>> FetchPerRepositoryAsync<T>(
        IReadOnlyList<GithubRepositorySummary> repositories,
        Func<GithubRepositorySummary, Task<IReadOnlyList<T>>> fetch,
        CancellationToken cancellationToken)
    {
        var items = new List<T>();
        var warnings = new List<string>();
        var skipped = 0;
        using var gate = new SemaphoreSlim(MaxConcurrentRepositoryRequests);

        var tasks = repositories.Select(async repository =>
        {
            await gate.WaitAsync(cancellationToken);
            try
            {
                var repositoryItems = await fetch(repository);
                lock (items)
                {
                    items.AddRange(repositoryItems);
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (PerRepositoryGitHubException exception)
            {
                Interlocked.Increment(ref skipped);
                lock (warnings)
                {
                    warnings.Add(exception.Message);
                }
            }
            finally
            {
                gate.Release();
            }
        });

        await Task.WhenAll(tasks);
        return new RepositoryActivityResult<T>(items, repositories.Count, skipped, warnings);
    }

    private async Task<HttpResponseMessage> SendRestAsync(string uri, CancellationToken cancellationToken)
    {
        var token = await _tokenStore.GetTokenAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(token))
        {
            throw new InvalidOperationException("Not authenticated.");
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        request.Headers.Add("X-GitHub-Api-Version", GitHubConstants.GitHubApiVersion);
        request.Headers.UserAgent.ParseAdd("Openza-Flow");
        return await _httpClient.SendAsync(request, cancellationToken);
    }

    private static PerRepositoryGitHubException PerRepositoryException(HttpStatusCode statusCode, string repository, string resource)
    {
        return new PerRepositoryGitHubException(
            $"Skipped {repository}: GitHub {resource} request returned {(int)statusCode}.");
    }

    private sealed class PerRepositoryGitHubException : Exception
    {
        public PerRepositoryGitHubException(string message)
            : base(message)
        {
        }
    }
}
