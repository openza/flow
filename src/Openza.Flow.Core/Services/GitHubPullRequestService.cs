using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Openza.Flow.Core.Models;

namespace Openza.Flow.Core.Services;

public sealed class GitHubPullRequestService
{
    private readonly HttpClient _httpClient;
    private readonly ITokenStore _tokenStore;

    public GitHubPullRequestService(HttpClient httpClient, ITokenStore tokenStore)
    {
        _httpClient = httpClient;
        _tokenStore = tokenStore;
    }

    public async Task<PaginatedResult<PullRequest>> GetReviewRequestsAsync(
        string? afterCursor = null,
        string? organization = null,
        CancellationToken cancellationToken = default)
    {
        var username = await GetRequiredUsernameAsync(cancellationToken);
        return await SearchPullRequestsInternalAsync(
            GitHubQueryBuilder.ReviewRequests(username, organization),
            afterCursor,
            cancellationToken);
    }

    public async Task<PaginatedResult<PullRequest>> GetCreatedPullRequestsAsync(
        string? afterCursor = null,
        string? organization = null,
        CancellationToken cancellationToken = default)
    {
        var username = await GetRequiredUsernameAsync(cancellationToken);
        return await SearchPullRequestsInternalAsync(
            GitHubQueryBuilder.CreatedPullRequests(username, organization),
            afterCursor,
            cancellationToken);
    }

    public async Task<PaginatedResult<ReviewedPullRequest>> GetReviewedPullRequestsAsync(
        string? afterCursor = null,
        string? organization = null,
        CancellationToken cancellationToken = default)
    {
        var username = await GetRequiredUsernameAsync(cancellationToken);
        var data = await SendGraphQlAsync(
            ReviewedPullRequestsQuery,
            new Dictionary<string, object?> { ["query"] = GitHubQueryBuilder.ReviewedPullRequests(username, organization), ["cursor"] = afterCursor },
            cancellationToken);

        return GitHubResponseMapper.MapReviewedPullRequestSearch(data.GetProperty("search"), username);
    }

    public async Task<IReadOnlyList<CreatedPullRequest>> GetRecentlyCreatedPullRequestsAsync(CancellationToken cancellationToken = default)
    {
        var username = await GetRequiredUsernameAsync(cancellationToken);
        var data = await SendGraphQlAsync(
            RecentlyCreatedPullRequestsQuery,
            new Dictionary<string, object?> { ["query"] = GitHubQueryBuilder.RecentlyCreatedPullRequests(username) },
            cancellationToken);

        return GitHubResponseMapper.MapCreatedPullRequests(data.GetProperty("search"));
    }

    public async Task<PaginatedResult<PullRequest>> SearchPullRequestsAsync(
        string query,
        string? afterCursor = null,
        string? organization = null,
        CancellationToken cancellationToken = default)
    {
        return await SearchPullRequestsInternalAsync(
            GitHubQueryBuilder.SearchPullRequests(query, organization),
            afterCursor,
            cancellationToken);
    }

    public async Task<PaginatedResult<PullRequest>> SearchReviewRequestsAsync(
        string query,
        string? afterCursor = null,
        string? organization = null,
        CancellationToken cancellationToken = default)
    {
        var username = await GetRequiredUsernameAsync(cancellationToken);
        return await SearchPullRequestsInternalAsync(
            GitHubQueryBuilder.SearchReviewRequests(username, query, organization),
            afterCursor,
            cancellationToken);
    }

    public async Task<PaginatedResult<PullRequest>> SearchCreatedPullRequestsAsync(
        string query,
        string? afterCursor = null,
        string? organization = null,
        CancellationToken cancellationToken = default)
    {
        var username = await GetRequiredUsernameAsync(cancellationToken);
        return await SearchPullRequestsInternalAsync(
            GitHubQueryBuilder.SearchCreatedPullRequests(username, query, organization),
            afterCursor,
            cancellationToken);
    }

    public async Task<IReadOnlyList<GithubOrganization>> GetOrganizationsAsync(CancellationToken cancellationToken = default)
    {
        var data = await SendGraphQlAsync(OrganizationsQuery, new Dictionary<string, object?>(), cancellationToken);
        return GitHubResponseMapper.MapOrganizations(data.GetProperty("viewer"));
    }

    private async Task<PaginatedResult<PullRequest>> SearchPullRequestsInternalAsync(
        string query,
        string? afterCursor,
        CancellationToken cancellationToken)
    {
        var data = await SendGraphQlAsync(
            PullRequestsQuery,
            new Dictionary<string, object?> { ["query"] = query, ["cursor"] = afterCursor },
            cancellationToken);

        var result = GitHubResponseMapper.MapPullRequestSearch(data.GetProperty("search"));
        var sorted = result.Items.OrderByDescending(pr => pr.UpdatedAt).ToList();
        return new PaginatedResult<PullRequest>(sorted, result.HasNextPage, result.EndCursor);
    }

    private async Task<JsonElement> SendGraphQlAsync(
        string query,
        IDictionary<string, object?> variables,
        CancellationToken cancellationToken)
    {
        var token = await _tokenStore.GetTokenAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(token))
        {
            throw new InvalidOperationException("Not authenticated.");
        }

        var payload = JsonSerializer.Serialize(new
        {
            query,
            variables
        });

        using var request = new HttpRequestMessage(HttpMethod.Post, GitHubConstants.GraphQlUrl)
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.UserAgent.ParseAdd("Openza-Flow");
        request.Headers.Add("X-GitHub-Api-Version", GitHubConstants.GitHubApiVersion);

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"GitHub request failed with status {(int)response.StatusCode}.");
        }

        using var document = JsonDocument.Parse(body);
        var root = document.RootElement.Clone();

        if (root.TryGetProperty("errors", out var errors) && errors.ValueKind == JsonValueKind.Array && errors.GetArrayLength() > 0)
        {
            var message = errors[0].TryGetProperty("message", out var messageElement)
                ? messageElement.GetString()
                : "GitHub GraphQL returned an error.";
            throw new InvalidOperationException(message);
        }

        return root.TryGetProperty("data", out var data)
            ? data
            : throw new InvalidOperationException("GitHub GraphQL response did not include data.");
    }

    private async Task<string> GetRequiredUsernameAsync(CancellationToken cancellationToken)
    {
        var username = await _tokenStore.GetUsernameAsync(cancellationToken);
        return string.IsNullOrWhiteSpace(username)
            ? throw new InvalidOperationException("Not authenticated.")
            : username;
    }

    private const string PullRequestsQuery = """
        query SearchPRs($query: String!, $cursor: String) {
          search(query: $query, type: ISSUE, first: 20, after: $cursor) {
            pageInfo { hasNextPage endCursor }
            nodes {
              ... on PullRequest {
                databaseId
                number
                title
                bodyText
                state
                url
                createdAt
                updatedAt
                isDraft
                baseRefName
                headRefName
                author { login avatarUrl url }
                repository { name owner { login } url }
                labels(first: 10) { nodes { name color description } }
              }
            }
          }
        }
        """;

    private const string ReviewedPullRequestsQuery = """
        query SearchReviewedPRs($query: String!, $cursor: String) {
          search(query: $query, type: ISSUE, first: 20, after: $cursor) {
            pageInfo { hasNextPage endCursor }
            nodes {
              ... on PullRequest {
                databaseId
                number
                title
                url
                state
                mergedAt
                updatedAt
                baseRefName
                headRefName
                author { login avatarUrl url }
                repository { name owner { login } url }
                reviews(last: 20) { nodes { state author { login } submittedAt } }
              }
            }
          }
        }
        """;

    private const string RecentlyCreatedPullRequestsQuery = """
        query SearchCreatedPRs($query: String!) {
          search(query: $query, type: ISSUE, first: 5) {
            nodes {
              ... on PullRequest {
                databaseId
                number
                title
                url
                state
                mergedAt
                createdAt
                baseRefName
                headRefName
                repository { name owner { login } url }
              }
            }
          }
        }
        """;

    private const string OrganizationsQuery = """
        query GetUserOrganizations {
          viewer {
            organizations(first: 100) {
              nodes { login name avatarUrl }
            }
          }
        }
        """;
}
