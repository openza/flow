using Openza.Flow.Core.Models;

namespace Openza.Flow.Core.Services;

public enum AgentSessionWorkspaceState
{
    Initial,
    Loading,
    Ready,
    Empty,
    PartialFailure,
    Unavailable
}

public interface IAgentEnvironmentEnablement
{
    bool IsAgentEnvironmentEnabled(string environmentId);
}

public interface IAgentSessionWorkspace : IAsyncDisposable
{
    event EventHandler? SnapshotChanged;

    IReadOnlyList<AgentEnvironment> Environments { get; }

    IReadOnlyList<AgentSessionSummary> Sessions { get; }

    AgentSessionWorkspaceState State { get; }

    string StatusMessage { get; }

    bool IsRefreshing { get; }

    DateTimeOffset? LastRefresh { get; }

    Task EnsureFreshAsync(TimeSpan maximumAge, CancellationToken cancellationToken = default);

    Task RefreshAsync(bool preserveExisting = true, CancellationToken cancellationToken = default);

    Task<AgentSessionPreview> LoadPreviewAsync(
        AgentSessionSummary session,
        CancellationToken cancellationToken = default);

    Task DeactivateAsync();
}

public sealed class AgentSessionWorkspace(
    IAgentSessionProvider provider,
    IAgentEnvironmentEnablement environmentEnablement) : IAgentSessionWorkspace
{
    private readonly object _gate = new();
    private IReadOnlyList<AgentEnvironment> _environments = [];
    private IReadOnlyList<AgentSessionSummary> _sessions = [];
    private HashSet<string> _snapshotEnvironmentIds = new(StringComparer.OrdinalIgnoreCase);
    private Task? _refreshTask;
    private CancellationTokenSource? _refreshCts;
    private volatile bool _preserveExistingForRefresh = true;
    private bool _disposed;

    public event EventHandler? SnapshotChanged;

    public IReadOnlyList<AgentEnvironment> Environments => _environments;

    public IReadOnlyList<AgentSessionSummary> Sessions => _sessions;

    public AgentSessionWorkspaceState State { get; private set; } = AgentSessionWorkspaceState.Initial;

    public string StatusMessage { get; private set; } = "Discovering coding agent environments…";

    public bool IsRefreshing { get; private set; }

    public DateTimeOffset? LastRefresh { get; private set; }

    public Task EnsureFreshAsync(TimeSpan maximumAge, CancellationToken cancellationToken = default)
    {
        if (CanUseSnapshot(maximumAge))
        {
            return Task.CompletedTask;
        }

        return RefreshAsync(preserveExisting: true, cancellationToken);
    }

    public Task RefreshAsync(bool preserveExisting = true, CancellationToken cancellationToken = default)
    {
        Task refreshTask;
        var publishClearedSnapshot = false;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var canReuseJustCompletedRefresh = preserveExisting
                && _refreshTask?.IsCompleted == true
                && CanUseSnapshot(TimeSpan.FromMilliseconds(100));
            if (_refreshTask is { IsCompleted: false } || canReuseJustCompletedRefresh)
            {
                if (!preserveExisting && _preserveExistingForRefresh)
                {
                    _preserveExistingForRefresh = false;
                    _sessions = [];
                    StatusMessage = "Refreshing agent sessions…";
                    publishClearedSnapshot = true;
                }

                refreshTask = _refreshTask!;
            }
            else
            {
                _refreshCts?.Dispose();
                _refreshCts = new CancellationTokenSource();
                _preserveExistingForRefresh = preserveExisting;
                _refreshTask = RefreshCoreAsync(preserveExisting, _refreshCts.Token);
                refreshTask = _refreshTask;
            }
        }

        if (publishClearedSnapshot)
        {
            RaiseSnapshotChanged();
        }

        return refreshTask.WaitAsync(cancellationToken);
    }

    public Task<AgentSessionPreview> LoadPreviewAsync(
        AgentSessionSummary session,
        CancellationToken cancellationToken = default) =>
        provider.LoadPreviewAsync(session, cancellationToken);

    public async Task DeactivateAsync()
    {
        Task? refreshTask;
        lock (_gate)
        {
            _refreshCts?.Cancel();
            refreshTask = _refreshTask;
        }

        if (refreshTask is not null)
        {
            try
            {
                await refreshTask;
            }
            catch (OperationCanceledException)
            {
            }
        }

        await provider.DisposeAsync();
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        await DeactivateAsync();
        _refreshCts?.Dispose();
    }

    private bool CanUseSnapshot(TimeSpan maximumAge)
    {
        if (LastRefresh is null || DateTimeOffset.Now - LastRefresh > maximumAge)
        {
            return false;
        }

        var currentlyEnabled = _environments
            .Where(environment => environment.IsAvailable && environmentEnablement.IsAgentEnvironmentEnabled(environment.Id))
            .Select(environment => environment.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return currentlyEnabled.SetEquals(_snapshotEnvironmentIds);
    }

    private async Task RefreshCoreAsync(bool preserveExisting, CancellationToken cancellationToken)
    {
        var hadSnapshot = preserveExisting && _sessions.Count > 0;
        var previousCount = _sessions.Count;
        if (!preserveExisting)
        {
            _sessions = [];
        }

        IsRefreshing = true;
        State = AgentSessionWorkspaceState.Loading;
        StatusMessage = hadSnapshot ? "Checking for updated sessions…" : "Discovering coding agent environments…";
        RaiseSnapshotChanged();

        try
        {
            await provider.DisposeAsync();
            _environments = await provider.ProbeEnvironmentsAsync(cancellationToken);
            RaiseSnapshotChanged();

            var configuredEnvironments = _environments
                .Where(environment => environmentEnablement.IsAgentEnvironmentEnabled(environment.Id))
                .ToList();
            var enabled = configuredEnvironments
                .Where(environment => environment.IsAvailable)
                .ToList();
            if (enabled.Count == 0)
            {
                var canPreserveSnapshot = hadSnapshot && ShouldPreserveExistingForCurrentRefresh();
                if (configuredEnvironments.Count == 0)
                {
                    _sessions = [];
                    LastRefresh = DateTimeOffset.Now;
                    _snapshotEnvironmentIds.Clear();
                    State = AgentSessionWorkspaceState.Unavailable;
                    StatusMessage = "No coding agent environments are enabled. Enable one in Settings.";
                }
                else
                {
                    State = canPreserveSnapshot ? AgentSessionWorkspaceState.PartialFailure : AgentSessionWorkspaceState.Unavailable;
                    StatusMessage = canPreserveSnapshot
                        ? $"Showing {previousCount:N0} sessions. No enabled coding agent environment responded."
                        : "No enabled coding agent environment is available. Check Settings.";
                }

                return;
            }

            StatusMessage = "Loading recent sessions…";
            RaiseSnapshotChanged();

            var failedProviders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var freshSessions = new List<AgentSessionSummary>();
            var publishedEnvironments = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            await foreach (var page in provider.EnumerateSessionsAsync(enabled, cancellationToken))
            {
                if (page.ErrorCategory is not null)
                {
                    failedProviders.Add(page.EnvironmentId);
                }
                else
                {
                    freshSessions.AddRange(page.Sessions);
                    var isFirstPageForEnvironment = publishedEnvironments.Add(page.EnvironmentId);
                    if (isFirstPageForEnvironment)
                    {
                        _sessions = AgentSessionUtilities.MergeAndSort(freshSessions);
                        RaiseSnapshotChanged();
                    }
                }
            }

            var allProvidersFailed = failedProviders.Count == enabled.Count;
            var canPreserveSnapshotAfterFailure = hadSnapshot && ShouldPreserveExistingForCurrentRefresh();
            if (!allProvidersFailed)
            {
                _sessions = AgentSessionUtilities.MergeAndSort(freshSessions);
                LastRefresh = DateTimeOffset.Now;
                _snapshotEnvironmentIds = enabled
                    .Select(environment => environment.Id)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
            }
            else if (!canPreserveSnapshotAfterFailure)
            {
                _sessions = [];
                LastRefresh = null;
                _snapshotEnvironmentIds.Clear();
            }

            if (allProvidersFailed && canPreserveSnapshotAfterFailure)
            {
                State = AgentSessionWorkspaceState.PartialFailure;
                StatusMessage = $"Showing {previousCount:N0} sessions. Agent session history could not be refreshed.";
            }
            else if (_sessions.Count == 0)
            {
                State = allProvidersFailed ? AgentSessionWorkspaceState.Unavailable : AgentSessionWorkspaceState.Empty;
                StatusMessage = allProvidersFailed
                    ? "Agent session history is unavailable in every enabled environment."
                    : "No interactive agent sessions were found.";
            }
            else if (failedProviders.Count > 0)
            {
                State = AgentSessionWorkspaceState.PartialFailure;
                StatusMessage = $"Showing sessions from {enabled.Count - failedProviders.Count} of {enabled.Count} environments.";
            }
            else
            {
                State = AgentSessionWorkspaceState.Ready;
                StatusMessage = $"{_sessions.Count:N0} sessions loaded.";
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            var canPreserveSnapshot = hadSnapshot && ShouldPreserveExistingForCurrentRefresh();
            State = canPreserveSnapshot ? AgentSessionWorkspaceState.PartialFailure : AgentSessionWorkspaceState.Unavailable;
            StatusMessage = canPreserveSnapshot
                ? $"Showing {previousCount:N0} sessions. Agent session history could not be refreshed."
                : "Coding agent environments could not be discovered.";
        }
        finally
        {
            IsRefreshing = false;
            RaiseSnapshotChanged();
        }
    }

    private bool ShouldPreserveExistingForCurrentRefresh() => _preserveExistingForRefresh;

    private void RaiseSnapshotChanged() => SnapshotChanged?.Invoke(this, EventArgs.Empty);
}
