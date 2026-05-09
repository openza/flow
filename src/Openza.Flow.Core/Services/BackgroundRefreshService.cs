using Openza.Flow.Core.Models;

namespace Openza.Flow.Core.Services;

public sealed class NewReviewRequestsEventArgs : EventArgs
{
    public NewReviewRequestsEventArgs(IReadOnlyList<PullRequest> pullRequests)
    {
        PullRequests = pullRequests;
    }

    public IReadOnlyList<PullRequest> PullRequests { get; }
}

public sealed class BackgroundRefreshService : IDisposable
{
    private readonly Func<CancellationToken, Task<IReadOnlyList<PullRequest>>> _fetchReviewRequests;
    private readonly TimeProvider _timeProvider;
    private readonly TimeSpan _refreshInterval;
    private readonly HashSet<int> _knownPullRequestIds = [];
    private CancellationTokenSource? _cts;
    private Task? _loopTask;
    private bool _hasLoadedOnce;

    public BackgroundRefreshService(
        Func<CancellationToken, Task<IReadOnlyList<PullRequest>>> fetchReviewRequests,
        TimeProvider? timeProvider = null,
        TimeSpan? refreshInterval = null)
    {
        _fetchReviewRequests = fetchReviewRequests;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _refreshInterval = refreshInterval ?? TimeSpan.FromMinutes(5);
    }

    public event EventHandler<NewReviewRequestsEventArgs>? NewReviewRequestsFound;

    public bool IsRunning => _cts is not null;

    public void Start()
    {
        if (_cts is not null)
        {
            return;
        }

        _cts = new CancellationTokenSource();
        _loopTask = RunAsync(_cts.Token);
    }

    public async Task StopAsync()
    {
        if (_cts is null)
        {
            return;
        }

        await _cts.CancelAsync();
        try
        {
            if (_loopTask is not null)
            {
                await _loopTask;
            }
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            _cts.Dispose();
            _cts = null;
            _loopTask = null;
        }
    }

    public async Task<IReadOnlyList<PullRequest>> RefreshOnceAsync(CancellationToken cancellationToken = default)
    {
        var pullRequests = await _fetchReviewRequests(cancellationToken);
        DetectNewPullRequests(pullRequests);
        return pullRequests;
    }

    public void Reset()
    {
        _knownPullRequestIds.Clear();
        _hasLoadedOnce = false;
    }

    public void Dispose()
    {
        _cts?.Cancel();
        _cts?.Dispose();
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await RefreshOnceAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                // Background refresh is opportunistic. The UI owns visible errors.
            }

            await Task.Delay(_refreshInterval, _timeProvider, cancellationToken);
        }
    }

    private void DetectNewPullRequests(IReadOnlyList<PullRequest> pullRequests)
    {
        var currentIds = pullRequests.Select(pr => pr.Id).ToHashSet();
        if (_hasLoadedOnce && _knownPullRequestIds.Count > 0)
        {
            var newPullRequests = pullRequests.Where(pr => !_knownPullRequestIds.Contains(pr.Id)).ToList();
            if (newPullRequests.Count > 0)
            {
                NewReviewRequestsFound?.Invoke(this, new NewReviewRequestsEventArgs(newPullRequests));
            }
        }

        _knownPullRequestIds.Clear();
        foreach (var id in currentIds)
        {
            _knownPullRequestIds.Add(id);
        }

        _hasLoadedOnce = true;
    }
}
