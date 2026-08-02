using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Openza.Flow.Core.Models;
using Openza.Flow.Core.Services;
using Openza.Flow.Services;

namespace Openza.Flow.ViewModels;

public enum AgentSessionsState
{
    Initial,
    Loading,
    Ready,
    Empty,
    NoResults,
    PartialFailure,
    Unavailable
}

public enum AgentSessionGroupingMode
{
    Date,
    Project
}

public sealed class AgentSessionsViewModel : ObservableObject
{
    private const int SessionPageSize = 100;
    private readonly IAgentSessionWorkspace _workspace;
    private readonly AppSettingsService _settings;
    private readonly ITerminalLauncher _terminalLauncher;
    private readonly List<AgentSessionSummary> _allSessions = [];
    private CancellationTokenSource? _previewCts;
    private string _searchText = string.Empty;
    private string? _selectedAgentId;
    private string? _selectedEnvironmentId;
    private string? _selectedSource;
    private AgentSessionGroupingMode _groupingMode;
    private AgentSessionListItem? _selectedSession;
    private AgentSessionPreview? _preview;
    private AgentSessionsState _state = AgentSessionsState.Initial;
    private string _statusMessage = "Open Agent Sessions to discover local session history.";
    private bool _isPreviewLoading;
    private bool _isActive;
    private int _visibleSessionLimit = SessionPageSize;
    private int _matchingSessionCount;
    private bool _suppressFilterApplication;

    public AgentSessionsViewModel(
        IAgentSessionWorkspace workspace,
        AppSettingsService settings,
        ITerminalLauncher terminalLauncher)
    {
        _workspace = workspace;
        _settings = settings;
        _terminalLauncher = terminalLauncher;
        ApplyWorkspaceSnapshot();
    }

    public ObservableCollection<AgentSessionGroup> Groups { get; } = [];

    public ObservableCollection<AgentEnvironmentFilterOption> EnvironmentFilters { get; } = [];

    public ObservableCollection<AgentSessionFilterOption> AgentFilters { get; } = [];

    public ObservableCollection<AgentSessionFilterOption> SourceFilters { get; } = [];

    public IReadOnlyList<AgentSessionGroupingOption> GroupingOptions { get; } =
    [
        new(AgentSessionGroupingMode.Date, "Date"),
        new(AgentSessionGroupingMode.Project, "Repository or folder")
    ];

    public IReadOnlyList<AgentEnvironment> Environments => _workspace.Environments;

    public AgentSessionsState State
    {
        get => _state;
        private set
        {
            if (SetProperty(ref _state, value))
            {
                OnPropertyChanged(nameof(IsLoading));
                OnPropertyChanged(nameof(HasSessions));
                OnPropertyChanged(nameof(ShowEmptyState));
            }
        }
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    public string SessionCountText => HasMoreSessions
        ? $"Showing {VisibleSessionCount:N0} of {_matchingSessionCount:N0}"
        : _matchingSessionCount != _allSessions.Count
            ? $"{_matchingSessionCount:N0} of {_allSessions.Count:N0} sessions"
            : $"{_allSessions.Count:N0} sessions";

    public int VisibleSessionCount => Math.Min(_matchingSessionCount, _visibleSessionLimit);

    public bool HasMoreSessions => VisibleSessionCount < _matchingSessionCount;

    public string LoadMoreText =>
        $"Show {Math.Min(SessionPageSize, _matchingSessionCount - VisibleSessionCount):N0} more";

    public bool IsLoading => State == AgentSessionsState.Loading;

    public bool HasSessions => Groups.Any(group => group.Count > 0);

    public bool ShowEmptyState => State is AgentSessionsState.Empty or AgentSessionsState.NoResults or AgentSessionsState.Unavailable;

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value))
            {
                _visibleSessionLimit = SessionPageSize;
                ApplyFiltersUnlessSuppressed();
            }
        }
    }

    public string? SelectedEnvironmentId
    {
        get => _selectedEnvironmentId;
        set
        {
            if (SetProperty(ref _selectedEnvironmentId, value))
            {
                _visibleSessionLimit = SessionPageSize;
                ApplyFiltersUnlessSuppressed();
            }
        }
    }

    public string? SelectedAgentId
    {
        get => _selectedAgentId;
        set
        {
            if (SetProperty(ref _selectedAgentId, value))
            {
                _visibleSessionLimit = SessionPageSize;
                ApplyFiltersUnlessSuppressed();
            }
        }
    }

    public string? SelectedSource
    {
        get => _selectedSource;
        set
        {
            if (SetProperty(ref _selectedSource, value))
            {
                _visibleSessionLimit = SessionPageSize;
                ApplyFiltersUnlessSuppressed();
            }
        }
    }

    public AgentSessionGroupingMode GroupingMode
    {
        get => _groupingMode;
        set
        {
            if (SetProperty(ref _groupingMode, value))
            {
                _visibleSessionLimit = SessionPageSize;
                ApplyFiltersUnlessSuppressed();
            }
        }
    }

    public AgentSessionListItem? SelectedSession
    {
        get => _selectedSession;
        private set
        {
            if (SetProperty(ref _selectedSession, value))
            {
                OnPropertyChanged(nameof(HasSelection));
                OnPropertyChanged(nameof(CopyableResumeCommand));
            }
        }
    }

    public bool HasSelection => SelectedSession is not null;

    public AgentSessionPreview? Preview
    {
        get => _preview;
        private set => SetProperty(ref _preview, value);
    }

    public bool IsPreviewLoading
    {
        get => _isPreviewLoading;
        private set => SetProperty(ref _isPreviewLoading, value);
    }

    public string? CopyableResumeCommand => SelectedSession is null
        ? null
        : _terminalLauncher.BuildCommand(SelectedSession.Summary, _settings.TerminalLaunchMode).CopyableCommand;

    public bool CanUseSnapshot(TimeSpan maximumAge)
    {
        return _workspace.LastRefresh is not null
            && DateTimeOffset.Now - _workspace.LastRefresh <= maximumAge;
    }

    public void RestoreSnapshotPresentation()
    {
        RebuildFilterOptions();
        if (SelectedAgentId is not null
            && AgentFilters.All(option => !string.Equals(option.Id, SelectedAgentId, StringComparison.OrdinalIgnoreCase)))
        {
            SelectedAgentId = null;
        }

        if (SelectedEnvironmentId is not null
            && EnvironmentFilters.All(option => !string.Equals(option.Id, SelectedEnvironmentId, StringComparison.OrdinalIgnoreCase)))
        {
            SelectedEnvironmentId = null;
        }

        if (SelectedSource is not null
            && SourceFilters.All(option => !string.Equals(option.Id, SelectedSource, StringComparison.OrdinalIgnoreCase)))
        {
            SelectedSource = null;
        }

        ApplyFilters();
    }

    public async Task RefreshAsync(bool preserveExisting = true, CancellationToken cancellationToken = default)
    {
        _previewCts?.Cancel();
        _previewCts = null;
        IsPreviewLoading = false;
        SelectedSession = null;
        Preview = null;
        await _workspace.RefreshAsync(preserveExisting, cancellationToken);
        ApplyWorkspaceSnapshot();
    }

    public async Task SelectAsync(AgentSessionListItem? item, CancellationToken cancellationToken = default)
    {
        _previewCts?.Cancel();
        SelectedSession = item;
        Preview = null;
        if (item is null)
        {
            _previewCts = null;
            IsPreviewLoading = false;
            return;
        }

        var previewCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _previewCts = previewCts;
        IsPreviewLoading = true;
        try
        {
            var preview = await _workspace.LoadPreviewAsync(item.Summary, previewCts.Token);
            if (ReferenceEquals(_previewCts, previewCts))
            {
                Preview = preview;
            }
        }
        catch (OperationCanceledException) when (previewCts.IsCancellationRequested)
        {
        }
        catch
        {
            if (ReferenceEquals(_previewCts, previewCts))
            {
                Preview = AgentSessionPreview.Unavailable(item.Summary.Key, "Preview could not be loaded.");
            }
        }
        finally
        {
            if (ReferenceEquals(_previewCts, previewCts))
            {
                IsPreviewLoading = false;
                _previewCts = null;
            }

            previewCts.Dispose();
        }
    }

    public Task<TerminalLaunchValidation> ValidateResumeAsync(CancellationToken cancellationToken = default) =>
        SelectedSession is null
            ? Task.FromResult(new TerminalLaunchValidation(false, "no_selection", "Select a session first."))
            : _terminalLauncher.ValidateAsync(SelectedSession.Summary, cancellationToken);

    public Task ResumeAsync(CancellationToken cancellationToken = default) =>
        SelectedSession is null
            ? Task.CompletedTask
            : _terminalLauncher.LaunchAsync(SelectedSession.Summary, _settings.TerminalLaunchMode, cancellationToken);

    public async Task DeactivateAsync()
    {
        SetActive(false);
        _previewCts?.Cancel();
        await Task.CompletedTask;
    }

    public async Task ActivateAsync(TimeSpan maximumAge, CancellationToken cancellationToken = default)
    {
        SetActive(true);
        ApplyWorkspaceSnapshot();
        await _workspace.EnsureFreshAsync(maximumAge, cancellationToken);
        ApplyWorkspaceSnapshot();
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
            _workspace.SnapshotChanged += OnWorkspaceSnapshotChanged;
            ApplyWorkspaceSnapshot();
        }
        else
        {
            _workspace.SnapshotChanged -= OnWorkspaceSnapshotChanged;
        }
    }

    public async Task SelectByKeyAsync(AgentSessionKey key, CancellationToken cancellationToken = default)
    {
        _suppressFilterApplication = true;
        try
        {
            SearchText = string.Empty;
            SelectedAgentId = null;
            SelectedEnvironmentId = null;
            SelectedSource = null;
            var sessionIndex = _allSessions.FindIndex(session => session.Key.Equals(key));
            if (sessionIndex >= _visibleSessionLimit)
            {
                _visibleSessionLimit = ((sessionIndex / SessionPageSize) + 1) * SessionPageSize;
            }
        }
        finally
        {
            _suppressFilterApplication = false;
        }

        ApplyFilters();
        var item = Groups.SelectMany(group => group)
            .FirstOrDefault(candidate => candidate.Summary.Key.Equals(key));
        await SelectAsync(item, cancellationToken);
    }

    public void LoadMoreSessions()
    {
        if (!HasMoreSessions)
        {
            return;
        }

        _visibleSessionLimit += SessionPageSize;
        ApplyFilters();
    }

    private void RebuildFilterOptions()
    {
        var enabled = Environments
            .Where(environment => environment.IsAvailable && _settings.IsAgentEnvironmentEnabled(environment.Id))
            .ToList();

        ReplaceFilterOptions(
            AgentFilters,
            [new(null, "All agents", _allSessions.Count),
             .. _allSessions
                 .GroupBy(ProviderId, StringComparer.OrdinalIgnoreCase)
                 .OrderBy(group => ProviderDisplayName(group.Key), StringComparer.OrdinalIgnoreCase)
                 .Select(group => new AgentSessionFilterOption(group.Key, ProviderDisplayName(group.Key), group.Count()))]);

        ReplaceEnvironmentFilters(
            [new(null, "All environments", _allSessions.Count),
             .. enabled.Select(environment => new AgentEnvironmentFilterOption(
                 environment.Id,
                 environment.DisplayName,
                 _allSessions.Count(session => session.Environment.Id.Equals(environment.Id, StringComparison.OrdinalIgnoreCase))))]);

        ReplaceFilterOptions(
            SourceFilters,
            [new(null, "All sources", _allSessions.Count),
             .. _allSessions
                 .GroupBy(session => session.Source, StringComparer.OrdinalIgnoreCase)
                 .OrderBy(group => SourceSortOrder(group.Key))
                 .ThenBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
                 .Select(group => new AgentSessionFilterOption(group.Key, group.Key, group.Count()))]);
    }

    private static void ReplaceFilterOptions(
        ObservableCollection<AgentSessionFilterOption> target,
        IReadOnlyList<AgentSessionFilterOption> options)
    {
        for (var index = 0; index < options.Count; index++)
        {
            var replacement = options[index];
            var existingIndex = IndexOf(target, replacement.Id);
            if (existingIndex < 0)
            {
                target.Insert(index, replacement);
                continue;
            }

            if (existingIndex != index)
            {
                target.Move(existingIndex, index);
            }

            target[index].Update(replacement.DisplayName, replacement.Count);
        }

        while (target.Count > options.Count)
        {
            target.RemoveAt(target.Count - 1);
        }
    }

    private void ReplaceEnvironmentFilters(IReadOnlyList<AgentEnvironmentFilterOption> options)
    {
        for (var index = 0; index < options.Count; index++)
        {
            var replacement = options[index];
            var existingIndex = IndexOf(EnvironmentFilters, replacement.Id);
            if (existingIndex < 0)
            {
                EnvironmentFilters.Insert(index, replacement);
                continue;
            }

            if (existingIndex != index)
            {
                EnvironmentFilters.Move(existingIndex, index);
            }

            EnvironmentFilters[index].Update(replacement.DisplayName, replacement.Count);
        }

        while (EnvironmentFilters.Count > options.Count)
        {
            EnvironmentFilters.RemoveAt(EnvironmentFilters.Count - 1);
        }
    }

    private static int IndexOf<TOption>(IEnumerable<TOption> options, string? id)
        where TOption : IAgentSessionFilterOption
    {
        var index = 0;
        foreach (var option in options)
        {
            if (string.Equals(option.Id, id, StringComparison.OrdinalIgnoreCase))
            {
                return index;
            }

            index++;
        }

        return -1;
    }

    private void ApplyFilters()
    {
        var matching = _allSessions.Where(session =>
            (SelectedAgentId is null || ProviderId(session).Equals(SelectedAgentId, StringComparison.OrdinalIgnoreCase))
            && (SelectedEnvironmentId is null || session.Environment.Id.Equals(SelectedEnvironmentId, StringComparison.OrdinalIgnoreCase))
            && (SelectedSource is null || session.Source.Equals(SelectedSource, StringComparison.OrdinalIgnoreCase))
            && AgentSessionUtilities.Matches(session, SearchText))
            .ToList();
        _matchingSessionCount = matching.Count;
        var filtered = matching.Take(_visibleSessionLimit).ToList();
        var selectedKey = SelectedSession?.Summary.Key;

        Groups.Clear();
        if (GroupingMode == AgentSessionGroupingMode.Project)
        {
            AddProjectGroups(filtered);
        }
        else
        {
            AddDateGroups(filtered);
        }

        if (selectedKey is not null)
        {
            SelectedSession = Groups
                .SelectMany(group => group)
                .FirstOrDefault(item => item.Summary.Key.Equals(selectedKey.Value));
            if (SelectedSession is null)
            {
                _previewCts?.Cancel();
                _previewCts = null;
                Preview = null;
                IsPreviewLoading = false;
            }
        }

        OnPropertyChanged(nameof(HasSessions));
        OnPropertyChanged(nameof(ShowEmptyState));
        OnPropertyChanged(nameof(SessionCountText));
        OnPropertyChanged(nameof(VisibleSessionCount));
        OnPropertyChanged(nameof(HasMoreSessions));
        OnPropertyChanged(nameof(LoadMoreText));
        if (_allSessions.Count > 0 && Groups.Count == 0)
        {
            State = AgentSessionsState.NoResults;
            StatusMessage = "No sessions match the current search and filters.";
        }
        else if (State == AgentSessionsState.NoResults && Groups.Count > 0)
        {
            State = AgentSessionsState.Ready;
            StatusMessage = $"{_allSessions.Count:N0} sessions loaded.";
        }
    }

    private void ApplyFiltersUnlessSuppressed()
    {
        if (!_suppressFilterApplication)
        {
            ApplyFilters();
        }
    }

    private void AddDateGroups(IEnumerable<AgentSessionSummary> sessions)
    {
        var grouped = sessions
            .GroupBy(session => AgentSessionUtilities.GetDateGroup(session.RecencyAt, DateTimeOffset.Now))
            .OrderBy(group => group.Key);
        foreach (var group in grouped)
        {
            Groups.Add(new AgentSessionGroup(
                GroupTitle(group.Key),
                null,
                group.Select(session => new AgentSessionListItem(session))));
        }
    }

    private void AddProjectGroups(IEnumerable<AgentSessionSummary> sessions)
    {
        var grouped = sessions
            .GroupBy(ProjectGroupingKey, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(group => group.Max(session => session.RecencyAt));
        foreach (var group in grouped)
        {
            var latest = group.OrderByDescending(session => session.RecencyAt).First();
            var root = ProjectRoot(latest);
            Groups.Add(new AgentSessionGroup(
                latest.Git?.RepositoryName ?? LastPathSegment(root),
                $"{latest.Environment.DisplayName} · {root}",
                group.OrderByDescending(session => session.RecencyAt)
                    .Select(session => new AgentSessionListItem(session))));
        }
    }

    private static string ProjectGroupingKey(AgentSessionSummary session) =>
        $"{session.Environment.Id}\0{ProjectRoot(session)}";

    private static string ProjectRoot(AgentSessionSummary session) =>
        (session.Git?.RepositoryRoot ?? session.WorkingDirectory).Trim().TrimEnd('/', '\\');

    private static string LastPathSegment(string path)
    {
        var separator = Math.Max(path.LastIndexOf('/'), path.LastIndexOf('\\'));
        return separator >= 0 ? path[(separator + 1)..] : path;
    }

    private void ReplaceSessions(IEnumerable<AgentSessionSummary> sessions)
    {
        var merged = AgentSessionUtilities.MergeAndSort(sessions);
        _allSessions.Clear();
        _allSessions.AddRange(merged);
        OnPropertyChanged(nameof(SessionCountText));
        RebuildFilterOptions();
        ApplyFilters();
    }

    private void OnWorkspaceSnapshotChanged(object? sender, EventArgs e) => ApplyWorkspaceSnapshot();

    private void ApplyWorkspaceSnapshot()
    {
        ReplaceSessions(_workspace.Sessions);
        State = _workspace.State switch
        {
            AgentSessionWorkspaceState.Loading => AgentSessionsState.Loading,
            AgentSessionWorkspaceState.Ready => AgentSessionsState.Ready,
            AgentSessionWorkspaceState.Empty => AgentSessionsState.Empty,
            AgentSessionWorkspaceState.PartialFailure => AgentSessionsState.PartialFailure,
            AgentSessionWorkspaceState.Unavailable => AgentSessionsState.Unavailable,
            _ => AgentSessionsState.Initial
        };
        StatusMessage = _workspace.StatusMessage;
    }

    private static string GroupTitle(AgentSessionDateGroup group) => group switch
    {
        AgentSessionDateGroup.Today => "Today",
        AgentSessionDateGroup.Yesterday => "Yesterday",
        AgentSessionDateGroup.ThisWeek => "This week",
        _ => "Older"
    };

    private static string ProviderId(AgentSessionSummary session)
    {
        var separator = session.Environment.Id.IndexOf(':');
        return separator > 0 ? session.Environment.Id[..separator] : session.Environment.Id;
    }

    private static string ProviderDisplayName(string providerId) =>
        DisplayText.ProviderName(providerId);

    private static int SourceSortOrder(string source) => source switch
    {
        "CLI" => 0,
        "App / IDE" => 1,
        "Desktop" => 2,
        _ => 3
    };
}

public sealed class AgentSessionGroup(string title, string? subtitle, IEnumerable<AgentSessionListItem> items)
    : ObservableCollection<AgentSessionListItem>(items)
{
    public string Title { get; } = title;
    public string? Subtitle { get; } = subtitle;
}

public interface IAgentSessionFilterOption
{
    string? Id { get; }
}

public sealed class AgentEnvironmentFilterOption(string? id, string displayName, int count)
    : ObservableObject, IAgentSessionFilterOption
{
    private string _displayName = displayName;
    private int _count = count;

    public string? Id { get; } = id;

    public string DisplayName
    {
        get => _displayName;
        private set => SetProperty(ref _displayName, value);
    }

    public int Count
    {
        get => _count;
        private set => SetProperty(ref _count, value);
    }

    public void Update(string displayName, int count)
    {
        DisplayName = displayName;
        Count = count;
    }

    public override string ToString() => DisplayName;
}

public sealed class AgentSessionFilterOption(string? id, string displayName, int count)
    : ObservableObject, IAgentSessionFilterOption
{
    private string _displayName = displayName;
    private int _count = count;

    public string? Id { get; } = id;

    public string DisplayName
    {
        get => _displayName;
        private set => SetProperty(ref _displayName, value);
    }

    public int Count
    {
        get => _count;
        private set
        {
            if (SetProperty(ref _count, value))
            {
                OnPropertyChanged(nameof(CountText));
            }
        }
    }

    public string CountText => Count.ToString("N0");

    public void Update(string displayName, int count)
    {
        DisplayName = displayName;
        Count = count;
    }

    public override string ToString() => DisplayName;
}

public sealed record AgentSessionGroupingOption(AgentSessionGroupingMode Mode, string DisplayName)
{
    public override string ToString() => DisplayName;
}

public sealed class AgentSessionListItem(AgentSessionSummary summary)
{
    public AgentSessionSummary Summary { get; } = summary;
    public string Title => Summary.Title;
    public string WorkingDirectory => Summary.WorkingDirectory;
    public string Environment => Summary.Environment.DisplayName;
    public string Source => Summary.Source;
    public string Folder => Summary.Git?.RepositoryName ?? DisplayText.LastPathSegment(Summary.WorkingDirectory);
    public string Branch => string.IsNullOrWhiteSpace(Summary.Git?.Branch)
        ? "Branch unavailable"
        : Summary.Git.Branch;
    public string Provider => DisplayText.ProviderName(Summary.Environment.Id);
    public string Recency => DisplayText.RelativeTime(Summary.RecencyAt);
    public string SessionId => Summary.Key.SessionId;

}
