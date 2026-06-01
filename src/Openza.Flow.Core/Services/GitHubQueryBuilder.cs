namespace Openza.Flow.Core.Services;

public static class GitHubQueryBuilder
{
    public static string ReviewRequests(string username, string? organization)
    {
        var query = $"type:pr state:open review-requested:{username}";
        query = ApplyOrganization(query, organization);
        return $"{query} sort:updated-desc";
    }

    public static string CreatedPullRequests(string username, string? organization)
    {
        var query = $"author:{username} type:pr state:open";
        query = ApplyOrganization(query, organization);
        return $"{query} sort:updated-desc";
    }

    public static string ReviewedPullRequests(string username, string? organization)
    {
        var query = $"type:pr reviewed-by:{username} -author:{username}";
        query = ApplyOrganization(query, organization);
        return $"{query} sort:updated-desc";
    }

    public static string RecentlyCreatedPullRequests(string username, string? organization)
    {
        var query = $"type:pr author:{username}";
        query = ApplyOrganization(query, organization);
        return $"{query} sort:created-desc";
    }

    public static string SearchPullRequests(string query, string? organization)
    {
        var trimmed = NormalizePullRequestSearch(query);
        return ApplyOrganization(trimmed, organization);
    }

    public static string SearchReviewRequests(string username, string query, string? organization)
    {
        var trimmed = NormalizePullRequestSearch(query);
        trimmed = EnsureQualifier(trimmed, "state:open");
        trimmed = EnsureQualifier(trimmed, $"review-requested:{username}");
        return ApplyOrganization(trimmed, organization);
    }

    public static string SearchCreatedPullRequests(string username, string query, string? organization)
    {
        var trimmed = NormalizePullRequestSearch(query);
        trimmed = EnsureQualifier(trimmed, "state:open");
        trimmed = EnsureQualifier(trimmed, $"author:{username}");
        return ApplyOrganization(trimmed, organization);
    }

    private static string ApplyOrganization(string query, string? organization)
    {
        return string.IsNullOrWhiteSpace(organization) ? query : $"{query} org:{organization.Trim()}";
    }

    private static string NormalizePullRequestSearch(string query)
    {
        var trimmed = string.IsNullOrWhiteSpace(query) ? "type:pr" : query.Trim();
        return trimmed.Contains("type:pr", StringComparison.OrdinalIgnoreCase)
            ? trimmed
            : $"{trimmed} type:pr";
    }

    private static string EnsureQualifier(string query, string qualifier)
    {
        return query.Contains(qualifier, StringComparison.OrdinalIgnoreCase)
            ? query
            : $"{query} {qualifier}";
    }
}
