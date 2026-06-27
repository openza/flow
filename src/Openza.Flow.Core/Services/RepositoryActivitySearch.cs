using Openza.Flow.Core.Models;

namespace Openza.Flow.Core.Services;

public static class RepositoryActivitySearch
{
    public static bool MatchesRelease(GithubRelease release, string? query)
    {
        return IsBlank(query)
            || Contains(release.Repository.FullName, query)
            || Contains(release.Name, query)
            || Contains(release.TagName, query)
            || Contains(release.Author, query);
    }

    public static bool MatchesWorkflowRun(GithubWorkflowRun run, string? query)
    {
        return IsBlank(query)
            || Contains(run.Repository.FullName, query)
            || Contains(run.WorkflowName, query)
            || Contains(run.DisplayTitle, query)
            || Contains(run.Branch, query)
            || Contains(run.Event, query)
            || Contains(run.Status, query)
            || Contains(run.Conclusion, query);
    }

    private static bool IsBlank(string? query)
    {
        return string.IsNullOrWhiteSpace(query);
    }

    private static bool Contains(string value, string? query)
    {
        return !string.IsNullOrWhiteSpace(query)
            && value.Contains(query.Trim(), StringComparison.OrdinalIgnoreCase);
    }
}
