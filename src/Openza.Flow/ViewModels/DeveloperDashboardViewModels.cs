using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Openza.Flow.Core.Models;
using Openza.Flow.Core.Services;
using Openza.Flow.Services;

namespace Openza.Flow.ViewModels;

public enum HomeSearchResultKind
{
    Session,
    Project
}

public sealed class HomeSearchResult
{
    public HomeSearchResultKind Kind { get; set; }

    public string Title { get; set; } = string.Empty;

    public string Subtitle { get; set; } = string.Empty;

    public AgentSessionKey? SessionKey { get; set; }

    public DeveloperProjectSummary? Project { get; set; }
}

public sealed class DashboardSessionItem
{
    public AgentSessionSummary Summary { get; set; } = null!;

    public string Title => Summary.Title;

    public string Folder => Summary.Git?.RepositoryName ?? DisplayText.LastPathSegment(Summary.WorkingDirectory);

    public string Branch => string.IsNullOrWhiteSpace(Summary.Git?.Branch) ? "Branch unavailable" : Summary.Git.Branch!;

    public string Environment => Summary.Environment.DisplayName;

    public string Source => Summary.Source;

    public string Recency => DisplayText.RelativeTime(Summary.RecencyAt);
}

public enum HomeAttentionKind
{
    ReviewRequest,
    WorkflowFailure
}

public sealed class HomeAttentionItem
{
    public HomeAttentionKind Kind { get; set; }

    public string Title { get; set; } = string.Empty;

    public string Detail { get; set; } = string.Empty;

    public string Status { get; set; } = string.Empty;

    public string Recency { get; set; } = string.Empty;

    public string Url { get; set; } = string.Empty;
}

public sealed class EnvironmentStatusItem
{
    public string Name { get; set; } = string.Empty;

    public string Status { get; set; } = string.Empty;

    public bool IsAvailable { get; set; }
}

public sealed class HomeViewModel : ObservableObject
{
    private readonly IAgentSessionWorkspace _workspace;
    private readonly GitHubWorkspaceState _github;
    private string _greeting = GreetingFor(DateTimeOffset.Now);
    private string _sessionStatus = "Discovering agent environments…";
    private bool _isRefreshing;
    private bool _isActive;
    private int _visibleItemLimit = 3;
    private IReadOnlyList<DeveloperProjectSummary> _projects = [];

    public HomeViewModel(IAgentSessionWorkspace workspace, GitHubWorkspaceState github)
    {
        _workspace = workspace;
        _github = github;
        Rebuild();
    }

    public ObservableCollection<DashboardSessionItem> ContinueWorking { get; } = [];

    public ObservableCollection<HomeAttentionItem> NeedsAttention { get; } = [];

    public ObservableCollection<EnvironmentStatusItem> EnvironmentStatuses { get; } = [];

    public event EventHandler? PresentationChanged;

    public string Greeting
    {
        get => _greeting;
        private set => SetProperty(ref _greeting, value);
    }

    public string SessionStatus
    {
        get => _sessionStatus;
        private set => SetProperty(ref _sessionStatus, value);
    }

    public bool IsRefreshing
    {
        get => _isRefreshing;
        private set => SetProperty(ref _isRefreshing, value);
    }

    public bool IsGitHubConnected => _github.IsAuthenticated;

    public string GitHubStatus => _github.IsAuthenticated ? "Connected" : "Not connected";

    public bool HasSessions => ContinueWorking.Count > 0;

    public bool HasAttention => NeedsAttention.Count > 0;

    public bool HasPartialFailure => _workspace.State is AgentSessionWorkspaceState.PartialFailure or AgentSessionWorkspaceState.Unavailable;

    public async Task ActivateAsync(CancellationToken cancellationToken = default)
    {
        SetActive(true);
        Greeting = GreetingFor(DateTimeOffset.Now);
        Rebuild();
        await _workspace.EnsureFreshAsync(TimeSpan.FromMinutes(5), cancellationToken);
        Rebuild();
    }

    public void SetActive(bool isActive)
    {
        if (_isActive == isActive)
        {
            return;
        }

        _isActive = isActive;
        if (isActive)
        {
            _workspace.SnapshotChanged += OnSourceChanged;
            _github.Changed += OnSourceChanged;
            Rebuild();
        }
        else
        {
            _workspace.SnapshotChanged -= OnSourceChanged;
            _github.Changed -= OnSourceChanged;
        }
    }

    public async Task RefreshSessionsAsync(CancellationToken cancellationToken = default)
    {
        await _workspace.RefreshAsync(preserveExisting: true, cancellationToken);
        Rebuild();
    }

    public void SetViewportHeight(double height)
    {
        var itemLimit = height switch
        {
            >= 780 => 6,
            >= 660 => 5,
            >= 560 => 4,
            _ => 3
        };
        if (_visibleItemLimit == itemLimit)
        {
            return;
        }

        _visibleItemLimit = itemLimit;
        Rebuild();
    }

    public IReadOnlyList<HomeSearchResult> Search(string? query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return [];
        }

        var sessions = _workspace.Sessions
            .Where(session => AgentSessionUtilities.Matches(session, query))
            .Take(5)
            .Select(session => new HomeSearchResult
            {
                Kind = HomeSearchResultKind.Session,
                Title = session.Title,
                Subtitle = $"Session · {session.Environment.DisplayName} · {session.Git?.RepositoryName ?? DisplayText.LastPathSegment(session.WorkingDirectory)}",
                SessionKey = session.Key
            });
        var projects = _projects
            .Where(project => DeveloperProjectUtilities.Matches(project, query))
            .Take(3)
            .Select(project => new HomeSearchResult
            {
                Kind = HomeSearchResultKind.Project,
                Title = project.DisplayName,
                Subtitle = $"Folder · {project.Environment.DisplayName} · {project.RootPath}",
                Project = project
            });
        return sessions.Concat(projects).Take(8).ToList();
    }

    public static string GreetingFor(DateTimeOffset value) => value.Hour switch
    {
        < 12 => "Good morning",
        < 17 => "Good afternoon",
        _ => "Good evening"
    };

    private void Rebuild()
    {
        _projects = DeveloperProjectUtilities.Aggregate(_workspace.Sessions);
        IsRefreshing = _workspace.IsRefreshing;
        SessionStatus = _workspace.StatusMessage;
        Replace(ContinueWorking, _workspace.Sessions.Take(_visibleItemLimit).Select(session => new DashboardSessionItem { Summary = session }));
        Replace(
            EnvironmentStatuses,
            _workspace.Environments.Select(environment => new EnvironmentStatusItem
            {
                Name = environment.Kind == AgentEnvironmentKind.Windows ? "Windows" : $"{environment.DisplayName} WSL",
                Status = environment.IsAvailable ? environment.CodexVersion is null ? "Available" : $"Codex {environment.CodexVersion}" : environment.StatusMessage ?? environment.Availability.ToString(),
                IsAvailable = environment.IsAvailable
            }));

        var reviewItems = _github.ReviewRequests.Select(item => new HomeAttentionItem
        {
            Kind = HomeAttentionKind.ReviewRequest,
            Title = "Pull request needs review",
            Detail = $"{item.Repository.FullName} #{item.Number} · {item.Title}",
            Status = item.Draft ? "Draft" : "Review",
            Recency = DisplayText.RelativeTime(item.UpdatedAt),
            Url = item.HtmlUrl
        });
        var failedRuns = _github.WorkflowRuns
            .Where(run => run.Conclusion is "failure" or "timed_out" or "cancelled" || run.Status == "action_required")
            .Select(run =>
            {
                var outcome = string.IsNullOrWhiteSpace(run.Conclusion) ? run.Status : run.Conclusion;
                var displayOutcome = outcome.Replace('_', ' ');
                return new HomeAttentionItem
                {
                    Kind = HomeAttentionKind.WorkflowFailure,
                    Title = $"Workflow {displayOutcome}",
                    Detail = $"{run.Repository.FullName} · {run.WorkflowName} #{run.RunNumber}",
                    Status = displayOutcome,
                    Recency = DisplayText.RelativeTime(run.UpdatedAt),
                    Url = run.HtmlUrl
                };
            });
        Replace(NeedsAttention, reviewItems.Concat(failedRuns).Take(_visibleItemLimit));

        OnPropertyChanged(nameof(IsGitHubConnected));
        OnPropertyChanged(nameof(GitHubStatus));
        OnPropertyChanged(nameof(HasSessions));
        OnPropertyChanged(nameof(HasAttention));
        OnPropertyChanged(nameof(HasPartialFailure));
        PresentationChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnSourceChanged(object? sender, EventArgs e) => Rebuild();

    internal static void Replace<T>(ObservableCollection<T> target, IEnumerable<T> values)
    {
        target.Clear();
        foreach (var value in values)
        {
            target.Add(value);
        }
    }
}

internal static class DisplayText
{
    public static string LastPathSegment(string path)
    {
        var normalized = path.TrimEnd('/', '\\');
        var separator = Math.Max(normalized.LastIndexOf('/'), normalized.LastIndexOf('\\'));
        return separator >= 0 ? normalized[(separator + 1)..] : normalized;
    }

    public static string RelativeTime(DateTimeOffset value)
    {
        var elapsed = DateTimeOffset.Now - value.ToLocalTime();
        return elapsed.TotalMinutes < 1 ? "Just now"
            : elapsed.TotalHours < 1 ? $"{Math.Max(1, (int)elapsed.TotalMinutes)} min ago"
            : elapsed.TotalDays < 1 ? $"{Math.Max(1, (int)elapsed.TotalHours)} hr ago"
            : elapsed.TotalDays < 7 ? $"{Math.Max(1, (int)elapsed.TotalDays)} days ago"
            : value.ToLocalTime().ToString("d MMM yyyy");
    }

    public static string ProviderName(string environmentId)
    {
        var separator = environmentId.IndexOf(':');
        var provider = separator > 0 ? environmentId[..separator] : environmentId;
        return provider.Equals("codex", StringComparison.OrdinalIgnoreCase)
            ? "Codex"
            : string.IsNullOrEmpty(provider)
                ? "Agent"
                : char.ToUpperInvariant(provider[0]) + provider[1..];
    }
}
