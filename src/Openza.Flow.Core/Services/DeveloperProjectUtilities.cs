using Openza.Flow.Core.Models;

namespace Openza.Flow.Core.Services;

public static class DeveloperProjectUtilities
{
    public static IReadOnlyList<DeveloperProjectSummary> Aggregate(IEnumerable<AgentSessionSummary> sessions) =>
        sessions
            .Select(session => new
            {
                Session = session,
                Root = NormalizeRoot(session.Git?.RepositoryRoot ?? session.WorkingDirectory)
            })
            .Where(item => item.Root.Length > 0)
            .GroupBy(
                item => new DeveloperProjectGroupingKey(
                    new DeveloperProjectKey(item.Session.Environment.Id, item.Root),
                    item.Session.Environment.Kind),
                DeveloperProjectGroupingKeyComparer.Instance)
            .Select(group =>
            {
                var latestItem = group
                    .OrderByDescending(item => item.Session.RecencyAt)
                    .First();
                var latest = latestItem.Session;
                var root = latestItem.Root;
                var projectKey = new DeveloperProjectKey(latest.Environment.Id, root);
                return new DeveloperProjectSummary(
                    projectKey,
                    latest.Git?.RepositoryName ?? LastPathSegment(root),
                    root,
                    latest.Git?.Branch,
                    latest.Environment,
                    latest,
                    group.Select(item => item.Session.Key).Distinct().Count(),
                    latest.RecencyAt);
            })
            .OrderByDescending(project => project.LastActivity)
            .ToList();

    public static bool Matches(DeveloperProjectSummary project, string? query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return true;
        }

        var comparison = StringComparison.OrdinalIgnoreCase;
        return project.DisplayName.Contains(query, comparison)
            || project.RootPath.Contains(query, comparison)
            || project.Environment.DisplayName.Contains(query, comparison)
            || (project.Branch?.Contains(query, comparison) ?? false)
            || project.LatestSession.Title.Contains(query, comparison);
    }

    private static string NormalizeRoot(string value)
    {
        var trimmed = value.Trim().TrimEnd('/', '\\');
        if (trimmed.Length == 2 && trimmed[1] == ':')
        {
            return $"{char.ToUpperInvariant(trimmed[0])}:\\";
        }

        return trimmed;
    }

    private static string LastPathSegment(string path)
    {
        var normalized = path.TrimEnd('/', '\\');
        var separator = Math.Max(normalized.LastIndexOf('/'), normalized.LastIndexOf('\\'));
        return separator >= 0 ? normalized[(separator + 1)..] : normalized;
    }

    private readonly record struct DeveloperProjectGroupingKey(
        DeveloperProjectKey ProjectKey,
        AgentEnvironmentKind EnvironmentKind);

    private sealed class DeveloperProjectGroupingKeyComparer : IEqualityComparer<DeveloperProjectGroupingKey>
    {
        public static DeveloperProjectGroupingKeyComparer Instance { get; } = new();

        public bool Equals(DeveloperProjectGroupingKey x, DeveloperProjectGroupingKey y) =>
            x.EnvironmentKind == y.EnvironmentKind
            && string.Equals(
                x.ProjectKey.EnvironmentId,
                y.ProjectKey.EnvironmentId,
                StringComparison.OrdinalIgnoreCase)
            && string.Equals(
                x.ProjectKey.RootPath,
                y.ProjectKey.RootPath,
                RootComparison(x.EnvironmentKind));

        public int GetHashCode(DeveloperProjectGroupingKey obj) =>
            HashCode.Combine(
                obj.EnvironmentKind,
                StringComparer.OrdinalIgnoreCase.GetHashCode(obj.ProjectKey.EnvironmentId),
                RootComparer(obj.EnvironmentKind).GetHashCode(obj.ProjectKey.RootPath));

        private static StringComparison RootComparison(AgentEnvironmentKind kind) =>
            kind == AgentEnvironmentKind.Wsl
                ? StringComparison.Ordinal
                : StringComparison.OrdinalIgnoreCase;

        private static StringComparer RootComparer(AgentEnvironmentKind kind) =>
            kind == AgentEnvironmentKind.Wsl
                ? StringComparer.Ordinal
                : StringComparer.OrdinalIgnoreCase;
    }
}
