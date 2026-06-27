using System.Text.Json;
using Openza.Flow.Core.Models;

namespace Openza.Flow.Core.Services;

public static class GitHubResponseMapper
{
    public static IReadOnlyList<GithubRepositorySummary> MapRepositorySummaries(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return root.EnumerateArray()
            .Where(node => node.ValueKind == JsonValueKind.Object)
            .Select(node =>
            {
                var owner = ReadObject(node, "owner");
                var ownerLogin = ReadString(owner, "login");
                var name = ReadString(node, "name");
                return new GithubRepositorySummary(
                    ReadString(node, "full_name", string.IsNullOrWhiteSpace(ownerLogin) ? name : $"{ownerLogin}/{name}"),
                    ownerLogin,
                    name,
                    ReadString(node, "html_url"),
                    ReadString(node, "default_branch"),
                    ReadDateTimeOffset(node, "pushed_at"));
            })
            .ToList();
    }

    public static IReadOnlyList<GithubRelease> MapReleases(JsonElement root, GithubRepositorySummary repository)
    {
        if (root.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var repo = new GithubRepository(repository.FullName, repository.HtmlUrl);
        return root.EnumerateArray()
            .Where(node => node.ValueKind == JsonValueKind.Object)
            .Select(node =>
            {
                var author = ReadObject(node, "author");
                var name = ReadString(node, "name");
                var tagName = ReadString(node, "tag_name");
                return new GithubRelease(
                    ReadLong(node, "id"),
                    repo,
                    string.IsNullOrWhiteSpace(name) ? tagName : name,
                    tagName,
                    ReadString(node, "html_url"),
                    ReadString(author, "login"),
                    ReadBool(node, "draft"),
                    ReadBool(node, "prerelease"),
                    ReadDateTimeOffset(node, "created_at"),
                    ReadNullableDateTimeOffset(node, "published_at"));
            })
            .ToList();
    }

    public static IReadOnlyList<GithubWorkflowRun> MapWorkflowRuns(JsonElement root, GithubRepositorySummary repository)
    {
        if (!root.TryGetProperty("workflow_runs", out var runs) || runs.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var repo = new GithubRepository(repository.FullName, repository.HtmlUrl);
        return runs.EnumerateArray()
            .Where(node => node.ValueKind == JsonValueKind.Object)
            .Select(node =>
            {
                var headCommit = ReadObject(node, "head_commit");
                return new GithubWorkflowRun(
                    ReadLong(node, "id"),
                    repo,
                    ReadString(node, "name"),
                    ReadString(node, "display_title", ReadString(node, "name")),
                    ReadString(node, "status"),
                    ReadString(node, "conclusion"),
                    ReadString(node, "event"),
                    ReadString(node, "head_branch"),
                    ReadString(node, "head_sha"),
                    ReadString(headCommit, "message"),
                    ReadLong(node, "run_number"),
                    ReadString(node, "html_url"),
                    ReadDateTimeOffset(node, "created_at"),
                    ReadDateTimeOffset(node, "updated_at"));
            })
            .ToList();
    }

    public static PaginatedResult<PullRequest> MapPullRequestSearch(JsonElement search)
    {
        return MapSearch(search, MapPullRequest);
    }

    public static PaginatedResult<ReviewedPullRequest> MapReviewedPullRequestSearch(JsonElement search, string currentUsername)
    {
        return MapSearch(search, node => MapReviewedPullRequest(node, currentUsername));
    }

    public static IReadOnlyList<CreatedPullRequest> MapCreatedPullRequests(JsonElement search)
    {
        if (!search.TryGetProperty("nodes", out var nodes) || nodes.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return nodes.EnumerateArray()
            .Where(node => node.ValueKind == JsonValueKind.Object)
            .Select(MapCreatedPullRequest)
            .ToList();
    }

    public static IReadOnlyList<GithubOrganization> MapOrganizations(JsonElement viewer)
    {
        if (!viewer.TryGetProperty("organizations", out var organizations)
            || !organizations.TryGetProperty("nodes", out var nodes)
            || nodes.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return nodes.EnumerateArray()
            .Where(node => node.ValueKind == JsonValueKind.Object)
            .Select(node => new GithubOrganization(
                ReadString(node, "login"),
                ReadString(node, "name", ReadString(node, "login")),
                ReadString(node, "avatarUrl")))
            .ToList();
    }

    public static PullRequest MapPullRequest(JsonElement node)
    {
        var repository = node.GetProperty("repository");
        var author = ReadObject(node, "author");

        return new PullRequest(
            ReadInt(node, "databaseId"),
            ReadInt(node, "number"),
            ReadString(node, "title"),
            ReadString(node, "bodyText"),
            ReadString(node, "state").ToLowerInvariant(),
            ReadString(node, "url"),
            ReadDateTimeOffset(node, "createdAt"),
            ReadDateTimeOffset(node, "updatedAt"),
            ReadBool(node, "isDraft"),
            MapUser(author),
            MapRepository(repository),
            MapLabels(node),
            ReadString(node, "baseRefName"),
            ReadString(node, "headRefName"));
    }

    public static ReviewedPullRequest MapReviewedPullRequest(JsonElement node, string currentUsername)
    {
        var repository = node.GetProperty("repository");
        var author = ReadObject(node, "author");
        var reviewedAt = ReadDateTimeOffset(node, "updatedAt");
        var reviewState = ReviewState.Pending;

        if (node.TryGetProperty("reviews", out var reviews)
            && reviews.TryGetProperty("nodes", out var reviewNodes)
            && reviewNodes.ValueKind == JsonValueKind.Array)
        {
            foreach (var review in reviewNodes.EnumerateArray().Reverse())
            {
                var reviewer = ReadObject(review, "author");
                if (!string.Equals(ReadString(reviewer, "login"), currentUsername, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                reviewedAt = ReadDateTimeOffset(review, "submittedAt", reviewedAt);
                reviewState = ReadString(review, "state") switch
                {
                    "APPROVED" => ReviewState.Approved,
                    "CHANGES_REQUESTED" => ReviewState.ChangesRequested,
                    "COMMENTED" => ReviewState.Commented,
                    _ => ReviewState.Pending
                };
                break;
            }
        }

        return new ReviewedPullRequest(
            ReadInt(node, "databaseId"),
            ReadInt(node, "number"),
            ReadString(node, "title"),
            ReadString(node, "url"),
            reviewedAt,
            reviewState,
            ReadMergeState(node),
            MapUser(author),
            MapRepository(repository),
            ReadString(node, "baseRefName"),
            ReadString(node, "headRefName"));
    }

    public static CreatedPullRequest MapCreatedPullRequest(JsonElement node)
    {
        var repository = node.GetProperty("repository");
        return new CreatedPullRequest(
            ReadInt(node, "databaseId"),
            ReadInt(node, "number"),
            ReadString(node, "title"),
            ReadString(node, "url"),
            ReadDateTimeOffset(node, "createdAt"),
            ReadMergeState(node),
            MapRepository(repository),
            ReadString(node, "baseRefName"),
            ReadString(node, "headRefName"));
    }

    private static PaginatedResult<T> MapSearch<T>(JsonElement search, Func<JsonElement, T> mapper)
    {
        if (search.ValueKind != JsonValueKind.Object)
        {
            return PaginatedResult<T>.Empty;
        }

        var hasNextPage = false;
        string? endCursor = null;
        if (search.TryGetProperty("pageInfo", out var pageInfo))
        {
            hasNextPage = ReadBool(pageInfo, "hasNextPage");
            endCursor = ReadNullableString(pageInfo, "endCursor");
        }

        if (!search.TryGetProperty("nodes", out var nodes) || nodes.ValueKind != JsonValueKind.Array)
        {
            return new PaginatedResult<T>([], hasNextPage, endCursor);
        }

        var items = nodes.EnumerateArray()
            .Where(node => node.ValueKind == JsonValueKind.Object)
            .Select(mapper)
            .ToList();

        return new PaginatedResult<T>(items, hasNextPage, endCursor);
    }

    private static GithubUser MapUser(JsonElement author)
    {
        return new GithubUser(
            ReadInt(author, "id"),
            ReadString(author, "login"),
            ReadString(author, "avatarUrl"),
            ReadString(author, "url"));
    }

    private static GithubRepository MapRepository(JsonElement repository)
    {
        var owner = ReadObject(repository, "owner");
        var fullName = $"{ReadString(owner, "login")}/{ReadString(repository, "name")}";
        return new GithubRepository(fullName, ReadString(repository, "url"));
    }

    private static IReadOnlyList<GithubLabel> MapLabels(JsonElement node)
    {
        if (!node.TryGetProperty("labels", out var labels)
            || !labels.TryGetProperty("nodes", out var nodes)
            || nodes.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return nodes.EnumerateArray()
            .Where(label => label.ValueKind == JsonValueKind.Object)
            .Select(label => new GithubLabel(
                ReadInt(label, "id"),
                ReadString(label, "name"),
                ReadString(label, "color", "000000"),
                ReadNullableString(label, "description")))
            .ToList();
    }

    private static MergeState ReadMergeState(JsonElement node)
    {
        if (node.TryGetProperty("mergedAt", out var mergedAt) && mergedAt.ValueKind != JsonValueKind.Null)
        {
            return MergeState.Merged;
        }

        return ReadString(node, "state").Equals("CLOSED", StringComparison.OrdinalIgnoreCase)
            || ReadString(node, "state").Equals("closed", StringComparison.OrdinalIgnoreCase)
            ? MergeState.Closed
            : MergeState.Open;
    }

    private static JsonElement ReadObject(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.Object
            ? value
            : default;
    }

    private static string ReadString(JsonElement element, string propertyName, string defaultValue = "")
    {
        return ReadNullableString(element, propertyName) ?? defaultValue;
    }

    private static string? ReadNullableString(JsonElement element, string propertyName)
    {
        return element.ValueKind == JsonValueKind.Object
            && element.TryGetProperty(propertyName, out var value)
            && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }

    private static int ReadInt(JsonElement element, string propertyName)
    {
        return element.ValueKind == JsonValueKind.Object
            && element.TryGetProperty(propertyName, out var value)
            && value.TryGetInt32(out var result)
            ? result
            : 0;
    }

    private static long ReadLong(JsonElement element, string propertyName)
    {
        return element.ValueKind == JsonValueKind.Object
            && element.TryGetProperty(propertyName, out var value)
            && value.TryGetInt64(out var result)
            ? result
            : 0;
    }

    private static bool ReadBool(JsonElement element, string propertyName)
    {
        return element.ValueKind == JsonValueKind.Object
            && element.TryGetProperty(propertyName, out var value)
            && value.ValueKind == JsonValueKind.True;
    }

    private static DateTimeOffset ReadDateTimeOffset(JsonElement element, string propertyName)
    {
        return ReadDateTimeOffset(element, propertyName, DateTimeOffset.MinValue);
    }

    private static DateTimeOffset ReadDateTimeOffset(JsonElement element, string propertyName, DateTimeOffset fallback)
    {
        var value = ReadNullableString(element, propertyName);
        return DateTimeOffset.TryParse(value, out var result) ? result : fallback;
    }

    private static DateTimeOffset? ReadNullableDateTimeOffset(JsonElement element, string propertyName)
    {
        var value = ReadNullableString(element, propertyName);
        return DateTimeOffset.TryParse(value, out var result) ? result : null;
    }
}
