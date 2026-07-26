namespace Openza.Flow.Core.Models;

public enum AgentEnvironmentKind
{
    Windows,
    Wsl
}

public enum AgentEnvironmentAvailability
{
    Available,
    Missing,
    Incompatible,
    TimedOut,
    Failed
}

public sealed record AgentEnvironment(
    string Id,
    AgentEnvironmentKind Kind,
    string DisplayName,
    string? DistributionName,
    string? ExecutablePath,
    string? CodexVersion,
    AgentEnvironmentAvailability Availability,
    string? StatusMessage = null)
{
    public bool IsAvailable => Availability == AgentEnvironmentAvailability.Available;
}

public readonly record struct AgentSessionKey(string EnvironmentId, string SessionId);

public sealed record AgentGitMetadata(string? Branch, string? RepositoryRoot, string? RemoteUrl)
{
    public string? RepositoryName
    {
        get
        {
            var value = !string.IsNullOrWhiteSpace(RemoteUrl) ? RemoteUrl : RepositoryRoot;
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            var normalized = value.TrimEnd('/', '\\');
            var lastSeparator = Math.Max(normalized.LastIndexOf('/'), normalized.LastIndexOf('\\'));
            var name = lastSeparator >= 0 ? normalized[(lastSeparator + 1)..] : normalized;
            return name.EndsWith(".git", StringComparison.OrdinalIgnoreCase) ? name[..^4] : name;
        }
    }
}

public sealed record AgentSessionSummary(
    AgentSessionKey Key,
    string Title,
    string WorkingDirectory,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset RecencyAt,
    string Source,
    AgentGitMetadata? Git,
    AgentEnvironment Environment);

public enum AgentSessionMessageRole
{
    User,
    Assistant
}

public sealed record AgentSessionMessage(AgentSessionMessageRole Role, string Text);

public sealed record AgentSessionPreview(
    AgentSessionKey Key,
    IReadOnlyList<AgentSessionMessage> Messages,
    bool IsAvailable = true,
    string? UnavailableReason = null)
{
    public static AgentSessionPreview Unavailable(AgentSessionKey key, string reason) =>
        new(key, [], false, reason);
}

public enum AgentSessionDateGroup
{
    Today,
    Yesterday,
    ThisWeek,
    Older
}

public enum TerminalLaunchMode
{
    NewTab,
    NewWindow
}

public sealed record TerminalLaunchCommand(string FileName, IReadOnlyList<string> Arguments, string CopyableCommand);

public readonly record struct DeveloperProjectKey(string EnvironmentId, string RootPath);

public sealed record DeveloperProjectSummary(
    DeveloperProjectKey Key,
    string DisplayName,
    string RootPath,
    string? Branch,
    AgentEnvironment Environment,
    AgentSessionSummary LatestSession,
    int SessionCount,
    DateTimeOffset LastActivity);
