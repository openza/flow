namespace Openza.Flow.Core.Models;

public enum ReviewState
{
    Approved,
    ChangesRequested,
    Commented,
    Pending
}

public enum MergeState
{
    Merged,
    Open,
    Closed
}

public sealed record GithubUser(
    int Id,
    string Login,
    string AvatarUrl,
    string HtmlUrl);

public sealed record GithubRepository(
    string FullName,
    string HtmlUrl)
{
    public string Owner => FullName.Split('/').FirstOrDefault() ?? string.Empty;

    public string Name => FullName.Split('/').LastOrDefault() ?? string.Empty;
}

public sealed record GithubRepositorySummary(
    string FullName,
    string Owner,
    string Name,
    string HtmlUrl,
    string DefaultBranch,
    DateTimeOffset PushedAt);

public sealed record GithubLabel(
    int Id,
    string Name,
    string Color,
    string? Description);

public sealed record PullRequest(
    int Id,
    int Number,
    string Title,
    string Body,
    string State,
    string HtmlUrl,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    bool Draft,
    GithubUser Author,
    GithubRepository Repository,
    IReadOnlyList<GithubLabel> Labels,
    string BaseRefName,
    string HeadRefName);

public sealed record ReviewedPullRequest(
    int Id,
    int Number,
    string Title,
    string HtmlUrl,
    DateTimeOffset ReviewedAt,
    ReviewState ReviewState,
    MergeState MergeState,
    GithubUser Author,
    GithubRepository Repository,
    string BaseRefName,
    string HeadRefName);

public sealed record CreatedPullRequest(
    int Id,
    int Number,
    string Title,
    string HtmlUrl,
    DateTimeOffset CreatedAt,
    MergeState MergeState,
    GithubRepository Repository,
    string BaseRefName,
    string HeadRefName);

public sealed record GithubOrganization(
    string Login,
    string Name,
    string AvatarUrl);

public sealed record GithubRelease(
    long Id,
    GithubRepository Repository,
    string Name,
    string TagName,
    string HtmlUrl,
    string Author,
    bool Draft,
    bool Prerelease,
    DateTimeOffset CreatedAt,
    DateTimeOffset? PublishedAt)
{
    public DateTimeOffset SortTimestamp => PublishedAt ?? CreatedAt;
}

public sealed record GithubWorkflowRun(
    long Id,
    GithubRepository Repository,
    string WorkflowName,
    string DisplayTitle,
    string Status,
    string Conclusion,
    string Event,
    string Branch,
    string CommitSha,
    string CommitTitle,
    long RunNumber,
    string HtmlUrl,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record PaginatedResult<T>(
    IReadOnlyList<T> Items,
    bool HasNextPage,
    string? EndCursor)
{
    public static PaginatedResult<T> Empty { get; } = new([], false, null);
}

public sealed record RepositoryActivityResult<T>(
    IReadOnlyList<T> Items,
    int ScannedRepositoryCount,
    int SkippedRepositoryCount,
    IReadOnlyList<string> Warnings);
