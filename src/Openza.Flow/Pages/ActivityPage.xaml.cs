using System.Collections.ObjectModel;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Openza.Flow.Core.Models;
using Openza.Flow.Core.Services;
using Openza.Flow.Services;
using Openza.Flow.ViewModels;
using Windows.ApplicationModel.DataTransfer;

namespace Openza.Flow.Pages;

public sealed partial class ActivityPage : Page
{
    private readonly ITokenStore _tokenStore;
    private readonly GitHubAuthService _authService;
    private readonly GitHubPullRequestService _pullRequestService;
    private readonly GitHubRepositoryActivityService _repositoryActivityService;
    private readonly IFlowCacheStore _cacheStore;
    private readonly AppSettingsService _settings;
    private readonly BackgroundRefreshService _backgroundRefresh;
    private readonly GitHubWorkspaceState _githubState;
    private readonly ObservableCollection<PrListItem> _pullRequests = [];
    private readonly ObservableCollection<PrListItem> _reviewedPullRequests = [];
    private readonly ObservableCollection<PrListItem> _recentlyCreatedPullRequests = [];
    private readonly ObservableCollection<RepositoryActivityListItem> _repositoryItems = [];
    private readonly DispatcherQueueTimer _searchTimer;
    private readonly DispatcherQueueTimer _refreshTimer;
    private IReadOnlyList<GithubRelease> _loadedReleases = [];
    private IReadOnlyList<GithubWorkflowRun> _loadedWorkflowRuns = [];
    private CancellationTokenSource? _loadCts;
    private CancellationTokenSource? _authCts;
    private DeviceCodeInfo? _currentDeviceCode;
    private ActivityMode _mode = ActivityMode.ReviewRequests;
    private ActivityMode _pullRequestMode = ActivityMode.ReviewRequests;
    private GitHubPageMode _pageMode = GitHubPageMode.PullRequests;
    private string _pullRequestSearch = string.Empty;
    private string _releaseSearch = string.Empty;
    private string _workflowSearch = string.Empty;
    private int _loadGeneration;
    private string? _endCursor;
    private bool _hasNextPage;
    private bool _isLoadingMore;
    private bool _initialized;
    private bool _isActive;
    private bool _isAuthenticated;
    private bool _changingMode;
    private bool _suppressOrganizationChanged;
    private string _username = "GitHub";
    private IReadOnlyList<GitHubOrganizationOption> _organizationOptions = [];
    private readonly Dictionary<GitHubPageMode, DateTimeOffset> _pageRefreshTimes = [];

    public ActivityPage(
        ITokenStore tokenStore,
        GitHubAuthService authService,
        GitHubPullRequestService pullRequestService,
        GitHubRepositoryActivityService repositoryActivityService,
        IFlowCacheStore cacheStore,
        AppSettingsService settings,
        BackgroundRefreshService backgroundRefresh,
        GitHubWorkspaceState githubState)
    {
        _tokenStore = tokenStore;
        _authService = authService;
        _pullRequestService = pullRequestService;
        _repositoryActivityService = repositoryActivityService;
        _cacheStore = cacheStore;
        _settings = settings;
        _backgroundRefresh = backgroundRefresh;
        _githubState = githubState;
        InitializeComponent();

        PullRequestList.ItemsSource = _pullRequests;
        ReviewedList.ItemsSource = _reviewedPullRequests;
        RecentlyCreatedList.ItemsSource = _recentlyCreatedPullRequests;
        RepositoryActivityList.ItemsSource = _repositoryItems;

        _searchTimer = DispatcherQueue.CreateTimer();
        _searchTimer.Interval = TimeSpan.FromMilliseconds(450);
        _searchTimer.Tick += async (_, _) =>
        {
            _searchTimer.Stop();
            if (_mode is ActivityMode.Releases or ActivityMode.WorkflowRuns)
            {
                ApplyRepositoryFilter();
            }
            else
            {
                await LoadPrimaryListAsync(useCacheFirst: false);
            }
        };

        _refreshTimer = DispatcherQueue.CreateTimer();
        _refreshTimer.Interval = TimeSpan.FromMinutes(5);
        _refreshTimer.Tick += async (_, _) =>
        {
            if (_isActive && _isAuthenticated)
            {
                await RefreshAsync();
            }
        };
    }

    public event EventHandler? AuthenticationChanged;

    public event EventHandler? GitHubContextChanged;

    public bool IsAuthenticated => _isAuthenticated;

    public string Username => _username;

    public IReadOnlyList<GitHubOrganizationOption> OrganizationOptions => _organizationOptions;

    public string? SelectedOrganizationLogin => SelectedOrganization;

    public async Task ActivateAsync()
    {
        _isActive = true;
        if (!_initialized)
        {
            _initialized = true;
            if (await _authService.EnsureStoredCredentialsAsync())
            {
                await ShowActivityAsync();
            }
            else
            {
                ShowAuth();
            }

            return;
        }

        if (_isAuthenticated
            && (!_pageRefreshTimes.TryGetValue(_pageMode, out var refreshedAt)
                || DateTimeOffset.Now - refreshedAt > TimeSpan.FromMinutes(5)))
        {
            await RefreshAsync();
        }
    }

    public void SetActive(bool active)
    {
        _isActive = active;
        if (!active)
        {
            _loadCts?.Cancel();
        }
    }

    public async Task RefreshAsync()
    {
        if (!_isAuthenticated)
        {
            return;
        }

        var requestedPageMode = _pageMode;
        var requestedMode = _mode;
        if (requestedMode is ActivityMode.Releases or ActivityMode.WorkflowRuns)
        {
            if (!await LoadRepositoryActivityAsync(requestedPageMode, requestedMode))
            {
                return;
            }
        }
        else
        {
            if (!await LoadPrimaryListAsync(useCacheFirst: false))
            {
                return;
            }

            var generation = _loadGeneration;
            var organization = SelectedOrganization;
            var token = _loadCts?.Token ?? CancellationToken.None;
            if (!await LoadSideListsAsync(generation, organization, token)
                || !IsCurrent(generation, organization, token))
            {
                return;
            }
        }

        if (_pageMode == requestedPageMode && _mode == requestedMode)
        {
            _pageRefreshTimes[requestedPageMode] = DateTimeOffset.Now;
        }
    }

    public async Task RefreshReviewRequestsForHomeAsync()
    {
        if (!_isAuthenticated)
        {
            return;
        }

        try
        {
            var result = await _pullRequestService.GetReviewRequestsAsync(organization: _settings.SelectedOrganization);
            _githubState.SetReviewRequests(result.Items);
            if (_settings.SelectedOrganization is null)
            {
                await _cacheStore.SetAsync(FlowCacheKeys.ReviewRequests, result.Items);
            }
        }
        catch (Exception exception)
        {
            AppLog.Write(exception);
        }
    }

    public async Task DisposeAsync()
    {
        _refreshTimer.Stop();
        _searchTimer.Stop();
        _loadCts?.Cancel();
        _authCts?.Cancel();
        await Task.CompletedTask;
    }

    public void SetPageMode(GitHubPageMode pageMode)
    {
        RememberCurrentSearch();
        var pageChanged = _pageMode != pageMode;
        if (pageChanged)
        {
            _loadCts?.Cancel();
            _loadGeneration++;
        }

        _changingMode = true;
        try
        {
            _pageMode = pageMode;
            _mode = pageMode switch
            {
                GitHubPageMode.Releases => ActivityMode.Releases,
                GitHubPageMode.WorkflowRuns => ActivityMode.WorkflowRuns,
                _ => _pullRequestMode
            };

            ActivitySelector.Visibility = pageMode == GitHubPageMode.PullRequests
                ? Visibility.Visible
                : Visibility.Collapsed;
            ReviewSelector.IsSelected = _mode == ActivityMode.ReviewRequests;
            CreatedSelector.IsSelected = _mode == ActivityMode.Created;
            UpdateModePresentation();
            SearchBox.Text = pageMode switch
            {
                GitHubPageMode.Releases => _releaseSearch,
                GitHubPageMode.WorkflowRuns => _workflowSearch,
                _ => _pullRequestSearch
            };
            if (_mode is ActivityMode.Releases or ActivityMode.WorkflowRuns)
            {
                ApplyRepositoryFilter();
            }

            if (pageChanged && _isAuthenticated)
            {
                var needsRefresh = !_pageRefreshTimes.TryGetValue(pageMode, out var refreshedAt)
                    || DateTimeOffset.Now - refreshedAt > TimeSpan.FromMinutes(5);
                SetLoading(needsRefresh);
            }
        }
        finally
        {
            _changingMode = false;
        }
    }

    public async Task SelectOrganizationAsync(string? login)
    {
        var selected = _organizationOptions.FirstOrDefault(item =>
            string.Equals(item.Login, login, StringComparison.OrdinalIgnoreCase))
            ?? _organizationOptions.FirstOrDefault();
        if (selected is null || string.Equals(SelectedOrganization, selected.Login, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _suppressOrganizationChanged = true;
        OrganizationCombo.SelectedItem = selected;
        _suppressOrganizationChanged = false;
        _settings.SelectedOrganization = selected.Login;
        _pageRefreshTimes.Clear();
        GitHubContextChanged?.Invoke(this, EventArgs.Empty);
        await RefreshAsync();
    }

    public async Task SignOutAsync()
    {
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "Sign out of GitHub?",
            Content = "GitHub features will be unavailable until you connect again. Agent Sessions will keep working.",
            PrimaryButtonText = "Sign out",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        _refreshTimer.Stop();
        await _backgroundRefresh.StopAsync();
        await _authService.SignOutAsync();
        await _cacheStore.ClearAsync();
        _pullRequests.Clear();
        _reviewedPullRequests.Clear();
        _recentlyCreatedPullRequests.Clear();
        _repositoryItems.Clear();
        _loadedReleases = [];
        _loadedWorkflowRuns = [];
        _organizationOptions = [];
        _pageRefreshTimes.Clear();
        ShowAuth();
    }

    private async Task ShowActivityAsync()
    {
        _isAuthenticated = true;
        _githubState.SetAuthentication(true);
        AuthView.Visibility = Visibility.Collapsed;
        ActivityContent.Visibility = Visibility.Visible;
        var username = await _tokenStore.GetUsernameAsync();
        _username = string.IsNullOrWhiteSpace(username) ? "GitHub" : username;
        UserMenuButton.Content = _username;
        await LoadOrganizationsAsync();
        var requestedPageMode = _pageMode;
        var requestedMode = _mode;
        bool loaded;
        if (requestedMode is ActivityMode.Releases or ActivityMode.WorkflowRuns)
        {
            loaded = await LoadRepositoryActivityAsync(requestedPageMode, requestedMode);
        }
        else
        {
            loaded = await LoadPrimaryListAsync(useCacheFirst: true);
            if (loaded)
            {
                var generation = _loadGeneration;
                var organization = SelectedOrganization;
                var token = _loadCts?.Token ?? CancellationToken.None;
                loaded = await LoadSideListsAsync(generation, organization, token)
                    && IsCurrent(generation, organization, token);
            }
        }

        if (loaded && _pageMode == requestedPageMode && _mode == requestedMode)
        {
            _pageRefreshTimes[requestedPageMode] = DateTimeOffset.Now;
        }

        _refreshTimer.Start();
        AuthenticationChanged?.Invoke(this, EventArgs.Empty);
        GitHubContextChanged?.Invoke(this, EventArgs.Empty);
    }

    private void ShowAuth()
    {
        _isAuthenticated = false;
        _githubState.SetAuthentication(false);
        AuthView.Visibility = Visibility.Visible;
        ActivityContent.Visibility = Visibility.Collapsed;
        _refreshTimer.Stop();
        AuthenticationChanged?.Invoke(this, EventArgs.Empty);
        GitHubContextChanged?.Invoke(this, EventArgs.Empty);
    }

    private async Task LoadOrganizationsAsync()
    {
        var cached = await _cacheStore.GetAsync<List<GithubOrganization>>(FlowCacheKeys.Organizations) ?? [];
        var organizations = cached;
        try
        {
            organizations = (await _pullRequestService.GetOrganizationsAsync()).ToList();
            await _cacheStore.SetAsync(FlowCacheKeys.Organizations, organizations);
        }
        catch (Exception exception)
        {
            if (cached.Count == 0)
            {
                AppLog.Write(exception);
            }
        }

        var items = new List<GitHubOrganizationOption> { new("All organizations", null, string.Empty) };
        items.AddRange(organizations.Select(org => new GitHubOrganizationOption(
            string.IsNullOrWhiteSpace(org.Name) ? org.Login : org.Name,
            org.Login,
            org.AvatarUrl)));
        _organizationOptions = items;
        _suppressOrganizationChanged = true;
        OrganizationCombo.ItemsSource = items;
        OrganizationCombo.SelectedItem = items.FirstOrDefault(item => item.Login == _settings.SelectedOrganization) ?? items[0];
        _suppressOrganizationChanged = false;
        GitHubContextChanged?.Invoke(this, EventArgs.Empty);
    }

    private async Task<bool> LoadPrimaryListAsync(bool useCacheFirst)
    {
        _loadCts?.Cancel();
        _loadCts?.Dispose();
        _loadCts = new CancellationTokenSource();
        var token = _loadCts.Token;
        var generation = ++_loadGeneration;
        SetLoading(true);
        HideError();
        try
        {
            var query = SearchBox.Text?.Trim() ?? string.Empty;
            var organization = SelectedOrganization;
            _endCursor = null;
            _hasNextPage = false;

            if (useCacheFirst && string.IsNullOrWhiteSpace(query) && organization is null)
            {
                var cacheKey = _mode == ActivityMode.Created ? FlowCacheKeys.CreatedPullRequests : FlowCacheKeys.ReviewRequests;
                var cached = await _cacheStore.GetAsync<List<PullRequest>>(cacheKey, token);
                if (cached is { Count: > 0 })
                {
                    Replace(_pullRequests, cached.Select(item => new PrListItem(item)));
                }
            }

            PaginatedResult<PullRequest> result;
            if (!string.IsNullOrWhiteSpace(query))
            {
                result = _mode == ActivityMode.Created
                    ? await _pullRequestService.SearchCreatedPullRequestsAsync(query, organization: organization, cancellationToken: token)
                    : await _pullRequestService.SearchReviewRequestsAsync(query, organization: organization, cancellationToken: token);
            }
            else
            {
                result = _mode == ActivityMode.Created
                    ? await _pullRequestService.GetCreatedPullRequestsAsync(organization: organization, cancellationToken: token)
                    : await _pullRequestService.GetReviewRequestsAsync(organization: organization, cancellationToken: token);
            }

            if (generation != _loadGeneration || token.IsCancellationRequested)
            {
                return false;
            }

            _endCursor = result.EndCursor;
            _hasNextPage = result.HasNextPage;
            Replace(_pullRequests, result.Items.Select(item => new PrListItem(item)));
            UpdatePullRequestTitle();
            if (_mode == ActivityMode.ReviewRequests)
            {
                _githubState.SetReviewRequests(result.Items);
            }

            if (string.IsNullOrWhiteSpace(query) && organization is null)
            {
                await _cacheStore.SetAsync(
                    _mode == ActivityMode.Created ? FlowCacheKeys.CreatedPullRequests : FlowCacheKeys.ReviewRequests,
                    result.Items,
                    token);
            }

            StatusText.Text = $"{result.Items.Count:N0} pull request{(result.Items.Count == 1 ? string.Empty : "s")}";
            LoadMoreButton.IsEnabled = _hasNextPage;
            UpdateEmptyState();
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
        catch (Exception exception)
        {
            ShowError(exception.Message);
            AppLog.Write(exception);
            return false;
        }
        finally
        {
            if (generation == _loadGeneration)
            {
                SetLoading(false);
            }
        }
    }

    private async Task<bool> LoadSideListsAsync(int generation, string? organization, CancellationToken token)
    {
        try
        {
            if (organization is null)
            {
                var cached = await _cacheStore.GetAsync<List<ReviewedPullRequest>>(FlowCacheKeys.ReviewedPullRequests, token);
                if (cached is { Count: > 0 } && IsCurrent(generation, organization, token))
                {
                    Replace(_reviewedPullRequests, cached.OrderByDescending(item => item.ReviewedAt).Select(item => new PrListItem(item)));
                }
            }

            var reviewed = (await _pullRequestService.GetReviewedPullRequestsAsync(organization: organization, cancellationToken: token))
                .Items.OrderByDescending(item => item.ReviewedAt).ToList();
            if (IsCurrent(generation, organization, token))
            {
                Replace(_reviewedPullRequests, reviewed.Select(item => new PrListItem(item)));
                if (organization is null)
                {
                    await _cacheStore.SetAsync(FlowCacheKeys.ReviewedPullRequests, reviewed, token);
                }
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            return false;
        }
        catch (OperationCanceledException exception)
        {
            AppLog.Write(exception);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            AppLog.Write(exception);
        }

        try
        {
            if (organization is null)
            {
                var cached = await _cacheStore.GetAsync<List<CreatedPullRequest>>(FlowCacheKeys.RecentlyCreatedPullRequests, token);
                if (cached is { Count: > 0 } && IsCurrent(generation, organization, token))
                {
                    Replace(_recentlyCreatedPullRequests, cached.OrderByDescending(item => item.CreatedAt).Select(item => new PrListItem(item)));
                }
            }

            var created = (await _pullRequestService.GetRecentlyCreatedPullRequestsAsync(organization, token))
                .OrderByDescending(item => item.CreatedAt).ToList();
            if (IsCurrent(generation, organization, token))
            {
                Replace(_recentlyCreatedPullRequests, created.Select(item => new PrListItem(item)));
                if (organization is null)
                {
                    await _cacheStore.SetAsync(FlowCacheKeys.RecentlyCreatedPullRequests, created, token);
                }
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            return false;
        }
        catch (OperationCanceledException exception)
        {
            AppLog.Write(exception);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            AppLog.Write(exception);
        }

        return !token.IsCancellationRequested && generation == _loadGeneration;
    }

    private bool IsCurrent(int generation, string? organization, CancellationToken token) =>
        !token.IsCancellationRequested && generation == _loadGeneration && organization == SelectedOrganization;

    private async Task LoadMoreAsync()
    {
        if (_isLoadingMore || !_hasNextPage || string.IsNullOrWhiteSpace(_endCursor))
        {
            return;
        }

        _isLoadingMore = true;
        var cursor = _endCursor;
        var query = SearchBox.Text?.Trim() ?? string.Empty;
        var organization = SelectedOrganization;
        var token = _loadCts?.Token ?? CancellationToken.None;
        try
        {
            PaginatedResult<PullRequest> result;
            if (!string.IsNullOrWhiteSpace(query))
            {
                result = _mode == ActivityMode.Created
                    ? await _pullRequestService.SearchCreatedPullRequestsAsync(query, cursor, organization, token)
                    : await _pullRequestService.SearchReviewRequestsAsync(query, cursor, organization, token);
            }
            else
            {
                result = _mode == ActivityMode.Created
                    ? await _pullRequestService.GetCreatedPullRequestsAsync(cursor, organization, token)
                    : await _pullRequestService.GetReviewRequestsAsync(cursor, organization, token);
            }

            _endCursor = result.EndCursor;
            _hasNextPage = result.HasNextPage;
            foreach (var item in result.Items.Select(item => new PrListItem(item)))
            {
                _pullRequests.Add(item);
            }

            LoadMoreButton.IsEnabled = _hasNextPage;
            StatusText.Text = $"{_pullRequests.Count:N0} pull requests";
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            ShowError(exception.Message);
        }
        finally
        {
            _isLoadingMore = false;
        }
    }

    private async Task<bool> LoadRepositoryActivityAsync(
        GitHubPageMode requestedPageMode,
        ActivityMode requestedMode)
    {
        _loadCts?.Cancel();
        _loadCts?.Dispose();
        _loadCts = new CancellationTokenSource();
        var token = _loadCts.Token;
        var generation = ++_loadGeneration;
        var organization = SelectedOrganization;
        SetLoading(true);
        HideError();
        try
        {
            if (requestedMode == ActivityMode.Releases)
            {
                var result = await _repositoryActivityService.GetRecentReleasesAsync(organization, token);
                if (!IsCurrentRepositoryRequest(generation, organization, token, requestedPageMode, requestedMode))
                {
                    return false;
                }

                _loadedReleases = result.Items;
                ApplyRepositoryFilter();
                RepositoryStatusText.Text = ActivityStatus("release", _repositoryItems.Count, result.ScannedRepositoryCount, result.SkippedRepositoryCount);
            }
            else
            {
                var result = await _repositoryActivityService.GetRecentWorkflowRunsAsync(organization, token);
                if (!IsCurrentRepositoryRequest(generation, organization, token, requestedPageMode, requestedMode))
                {
                    return false;
                }

                _loadedWorkflowRuns = result.Items;
                _githubState.SetWorkflowRuns(result.Items);
                ApplyRepositoryFilter();
                RepositoryStatusText.Text = ActivityStatus("workflow run", _repositoryItems.Count, result.ScannedRepositoryCount, result.SkippedRepositoryCount);
            }

            UpdateEmptyState();
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
        catch (Exception exception)
        {
            ShowError(exception.Message);
            AppLog.Write(exception);
            return false;
        }
        finally
        {
            if (generation == _loadGeneration
                && _pageMode == requestedPageMode
                && _mode == requestedMode)
            {
                SetLoading(false);
            }
        }
    }

    private bool IsCurrentRepositoryRequest(
        int generation,
        string? organization,
        CancellationToken token,
        GitHubPageMode requestedPageMode,
        ActivityMode requestedMode) =>
        IsCurrent(generation, organization, token)
        && _pageMode == requestedPageMode
        && _mode == requestedMode;

    private void ApplyRepositoryFilter()
    {
        var query = SearchBox.Text?.Trim() ?? string.Empty;
        if (_mode == ActivityMode.Releases)
        {
            Replace(_repositoryItems, _loadedReleases
                .Where(item => RepositoryActivitySearch.MatchesRelease(item, query))
                .Select(item => new RepositoryActivityListItem(item)));
        }
        else if (_mode == ActivityMode.WorkflowRuns)
        {
            var conclusion = (ConclusionCombo.SelectedItem as ComboBoxItem)?.Tag as string;
            Replace(_repositoryItems, _loadedWorkflowRuns
                .Where(item => RepositoryActivitySearch.MatchesWorkflowRun(item, query))
                .Where(item => string.IsNullOrWhiteSpace(conclusion)
                    || string.Equals(item.Conclusion, conclusion, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(item.Status, conclusion, StringComparison.OrdinalIgnoreCase))
                .Select(item => new RepositoryActivityListItem(item)));
        }

        RepositoryActivityTitle.Text = _mode == ActivityMode.WorkflowRuns
            ? $"Workflow runs ({_repositoryItems.Count:N0})"
            : $"Releases ({_repositoryItems.Count:N0})";
    }

    private async void OnActivitySelectionChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
    {
        if (_changingMode)
        {
            return;
        }

        _mode = sender.SelectedItem == CreatedSelector ? ActivityMode.Created
            : ActivityMode.ReviewRequests;
        _pullRequestMode = _mode;
        UpdateModePresentation();
        PullRequestContent.ChangeView(null, 0, null, disableAnimation: true);
        await RefreshAsync();
    }

    private void UpdateModePresentation()
    {
        var repositoryMode = _mode is ActivityMode.Releases or ActivityMode.WorkflowRuns;
        PullRequestContent.Visibility = repositoryMode ? Visibility.Collapsed : Visibility.Visible;
        RepositoryContent.Visibility = repositoryMode ? Visibility.Visible : Visibility.Collapsed;
        ConclusionCombo.Visibility = _mode == ActivityMode.WorkflowRuns ? Visibility.Visible : Visibility.Collapsed;
        ReleaseHeader.Visibility = _mode == ActivityMode.Releases ? Visibility.Visible : Visibility.Collapsed;
        WorkflowHeader.Visibility = _mode == ActivityMode.WorkflowRuns ? Visibility.Visible : Visibility.Collapsed;
        var createdMode = _mode == ActivityMode.Created;
        RecentActivitySurface.Visibility = repositoryMode ? Visibility.Collapsed : Visibility.Visible;
        ReviewedList.Visibility = createdMode ? Visibility.Collapsed : Visibility.Visible;
        RecentlyCreatedList.Visibility = createdMode ? Visibility.Visible : Visibility.Collapsed;
        RecentActivityTitle.Text = createdMode ? "Recently created" : "Recently reviewed";
        PageHeaderTitle.Text = _mode switch
        {
            ActivityMode.Releases => "Releases",
            ActivityMode.WorkflowRuns => "Workflow Runs",
            _ => "Pull Requests"
        };
        PageHeaderSubtitle.Text = _mode switch
        {
            ActivityMode.Releases => "Track published and prerelease versions across repositories.",
            ActivityMode.WorkflowRuns => "Review GitHub Actions workflow results across repositories.",
            _ => "Review and track pull requests across GitHub."
        };
        UpdatePullRequestTitle();
        RepositoryActivityTitle.Text = _mode == ActivityMode.WorkflowRuns
            ? $"Workflow runs ({_repositoryItems.Count:N0})"
            : $"Releases ({_repositoryItems.Count:N0})";
        RepositoryStatusText.Text = _mode switch
        {
            ActivityMode.Releases when _loadedReleases.Count > 0 => $"{_loadedReleases.Count:N0} cached releases",
            ActivityMode.WorkflowRuns when _loadedWorkflowRuns.Count > 0 => $"{_loadedWorkflowRuns.Count:N0} cached workflow runs",
            ActivityMode.Releases => "Releases have not been loaded yet",
            ActivityMode.WorkflowRuns => "Workflow runs have not been loaded yet",
            _ => RepositoryStatusText.Text
        };
        SearchBox.PlaceholderText = _mode switch
        {
            ActivityMode.Releases => "Search releases",
            ActivityMode.WorkflowRuns => "Search workflow runs",
            _ => "Search pull requests"
        };
        AutomationProperties.SetName(SearchBox, SearchBox.PlaceholderText);
    }

    private void UpdatePullRequestTitle()
    {
        PageTitle.Text = _mode == ActivityMode.Created
            ? $"Created pull requests ({_pullRequests.Count:N0})"
            : $"Pull requests to review ({_pullRequests.Count:N0})";
    }

    private void OnConclusionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_mode == ActivityMode.WorkflowRuns)
        {
            ApplyRepositoryFilter();
            UpdateEmptyState();
        }
    }

    private void OnSearchTextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (args.Reason != AutoSuggestionBoxTextChangeReason.UserInput)
        {
            return;
        }

        RememberCurrentSearch();
        _searchTimer.Stop();
        _searchTimer.Start();
    }

    private void RememberCurrentSearch()
    {
        switch (_pageMode)
        {
            case GitHubPageMode.Releases:
                _releaseSearch = SearchBox.Text ?? string.Empty;
                break;
            case GitHubPageMode.WorkflowRuns:
                _workflowSearch = SearchBox.Text ?? string.Empty;
                break;
            default:
                _pullRequestSearch = SearchBox.Text ?? string.Empty;
                break;
        }
    }

    private async void OnOrganizationChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressOrganizationChanged)
        {
            return;
        }

        if (OrganizationCombo.SelectedItem is GitHubOrganizationOption item)
        {
            _settings.SelectedOrganization = item.Login;
            _pageRefreshTimes.Clear();
            GitHubContextChanged?.Invoke(this, EventArgs.Empty);
            await RefreshAsync();
        }
    }

    private async void OnRefreshClicked(object sender, RoutedEventArgs e) => await RefreshAsync();

    private async void OnLoadMoreClicked(object sender, RoutedEventArgs e) => await LoadMoreAsync();

    private async void OnPrOpenRequested(object? sender, PrListItem item)
    {
        if (Uri.TryCreate(item.HtmlUrl, UriKind.Absolute, out var uri))
        {
            await Windows.System.Launcher.LaunchUriAsync(uri);
        }
    }

    private async void OnRepositoryActivityOpenRequested(object? sender, RepositoryActivityListItem item)
    {
        if (Uri.TryCreate(item.HtmlUrl, UriKind.Absolute, out var uri))
        {
            await Windows.System.Launcher.LaunchUriAsync(uri);
        }
    }

    private void OnPrNumberCopyRequested(object? sender, PrListItem item)
    {
        CopyText(item.Number.ToString());
        StatusText.Text = $"Copied {item.DisplayNumber} to clipboard";
    }

    private async void OnGitHubOAuthClicked(object sender, RoutedEventArgs e)
    {
        _authCts?.Cancel();
        _authCts?.Dispose();
        _authCts = new CancellationTokenSource();
        DeviceCodePanel.Visibility = Visibility.Collapsed;
        AuthInfoBar.IsOpen = true;
        AuthInfoBar.Severity = InfoBarSeverity.Informational;
        AuthInfoBar.Message = "Requesting a GitHub device code…";
        try
        {
            var code = await _authService.RequestDeviceCodeAsync(_authCts.Token);
            _currentDeviceCode = code;
            DeviceCodeBox.Text = code.UserCode;
            DeviceCodePanel.Visibility = Visibility.Visible;
            AuthInfoBar.Message = "Waiting for GitHub authorization…";
            await Windows.System.Launcher.LaunchUriAsync(new Uri(code.VerificationUri));
            var result = await _authService.PollForTokenAsync(code, cancellationToken: _authCts.Token);
            var validation = await _authService.ValidateAndSaveTokenAsync(result.AccessToken, _authCts.Token);
            if (!validation.IsValid)
            {
                AuthInfoBar.Severity = InfoBarSeverity.Error;
                AuthInfoBar.Message = validation.ErrorMessage ?? "GitHub sign-in failed.";
                return;
            }

            DeviceCodePanel.Visibility = Visibility.Collapsed;
            await ShowActivityAsync();
        }
        catch (OperationCanceledException)
        {
            AuthInfoBar.Message = "GitHub sign-in was cancelled.";
        }
        catch (Exception exception)
        {
            AuthInfoBar.Severity = InfoBarSeverity.Error;
            AuthInfoBar.Message = exception.Message;
            AppLog.Write(exception);
        }
    }

    private async void OnPatSignInClicked(object sender, RoutedEventArgs e)
    {
        var token = PatBox.Password.Trim();
        if (string.IsNullOrWhiteSpace(token))
        {
            AuthInfoBar.IsOpen = true;
            AuthInfoBar.Severity = InfoBarSeverity.Error;
            AuthInfoBar.Message = "Enter a GitHub token.";
            return;
        }

        if (!token.StartsWith("ghp_", StringComparison.OrdinalIgnoreCase)
            && !token.StartsWith("github_pat_", StringComparison.OrdinalIgnoreCase))
        {
            AuthInfoBar.IsOpen = true;
            AuthInfoBar.Severity = InfoBarSeverity.Error;
            AuthInfoBar.Message = "Token should start with ghp_ or github_pat_.";
            return;
        }

        AuthInfoBar.IsOpen = true;
        AuthInfoBar.Severity = InfoBarSeverity.Informational;
        AuthInfoBar.Message = "Validating token…";
        var validation = await _authService.ValidateAndSaveTokenAsync(token);
        if (!validation.IsValid)
        {
            AuthInfoBar.Severity = InfoBarSeverity.Error;
            AuthInfoBar.Message = validation.ErrorMessage ?? "Token validation failed.";
            return;
        }

        PatBox.Password = string.Empty;
        await ShowActivityAsync();
    }

    private void OnCancelDeviceFlowClicked(object sender, RoutedEventArgs e)
    {
        _authCts?.Cancel();
        DeviceCodePanel.Visibility = Visibility.Collapsed;
    }

    private void OnCopyDeviceCodeClicked(object sender, RoutedEventArgs e) => CopyText(DeviceCodeBox.Text);

    private async void OnOpenDevicePageClicked(object sender, RoutedEventArgs e)
    {
        if (_currentDeviceCode is not null)
        {
            await Windows.System.Launcher.LaunchUriAsync(new Uri(_currentDeviceCode.VerificationUri));
        }
    }

    private async void OnCreateTokenClicked(object sender, RoutedEventArgs e) =>
        await Windows.System.Launcher.LaunchUriAsync(new Uri("https://github.com/settings/tokens/new?description=Openza%20Flow&scopes=repo,read:user,read:org"));

    private async void OnSignOutClicked(object sender, RoutedEventArgs e) => await SignOutAsync();

    private void OnPullRequestListLoaded(object sender, RoutedEventArgs e)
    {
        if (FindDescendant<ScrollViewer>(PullRequestList) is { } scrollViewer)
        {
            scrollViewer.ViewChanged += async (_, _) =>
            {
                if (!_isLoadingMore && _hasNextPage && scrollViewer.ScrollableHeight - scrollViewer.VerticalOffset < 260)
                {
                    await LoadMoreAsync();
                }
            };
        }
    }

    private void SetLoading(bool loading)
    {
        LoadingRing.IsActive = loading;
        ActivityProgress.Visibility = loading ? Visibility.Visible : Visibility.Collapsed;
        if (loading)
        {
            if (_mode == ActivityMode.Releases)
            {
                RepositoryStatusText.Text = "Updating releases…";
            }
            else if (_mode == ActivityMode.WorkflowRuns)
            {
                RepositoryStatusText.Text = "Updating workflow runs…";
            }
            else
            {
                StatusText.Text = "Updating pull requests…";
            }
        }

        SearchBox.IsEnabled = !loading;
        OrganizationCombo.IsEnabled = !loading;
        LoadMoreButton.IsEnabled = !loading && _hasNextPage;
        UpdateEmptyState(loading);
    }

    private void UpdateEmptyState(bool loading = false)
    {
        var repositoryMode = _mode is ActivityMode.Releases or ActivityMode.WorkflowRuns;
        var count = repositoryMode ? _repositoryItems.Count : _pullRequests.Count;
        var showEmpty = !loading && ErrorState.Visibility != Visibility.Visible && count == 0;
        EmptyState.Visibility = !repositoryMode && showEmpty ? Visibility.Visible : Visibility.Collapsed;
        RepositoryEmptyState.Visibility = repositoryMode && showEmpty ? Visibility.Visible : Visibility.Collapsed;
        if (repositoryMode)
        {
            RepositoryEmptyTitle.Text = _mode == ActivityMode.Releases ? "No releases found" : "No workflow runs found";
            RepositoryEmptyMessage.Text = string.IsNullOrWhiteSpace(SearchBox.Text)
                ? _mode == ActivityMode.Releases
                    ? "No releases were found in the selected GitHub scope."
                    : "No workflow runs were found in the selected GitHub scope."
                : "No items match your search.";
        }
        else
        {
            EmptyTitle.Text = _mode == ActivityMode.Created ? "No pull requests created by you" : "No pull requests need your review";
            EmptyMessage.Text = string.IsNullOrWhiteSpace(SearchBox.Text)
                ? _mode == ActivityMode.Created
                    ? "Pull requests you create will appear here."
                    : "You’re all caught up."
                : "No pull requests match your search.";
        }
    }

    private void ShowEmpty(string title, string message)
    {
        var repositoryMode = _mode is ActivityMode.Releases or ActivityMode.WorkflowRuns;
        EmptyTitle.Text = title;
        EmptyMessage.Text = message;
        RepositoryEmptyTitle.Text = title;
        RepositoryEmptyMessage.Text = message;
        EmptyState.Visibility = repositoryMode ? Visibility.Collapsed : Visibility.Visible;
        RepositoryEmptyState.Visibility = repositoryMode ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ShowError(string message)
    {
        ErrorMessage.Text = message;
        ErrorState.Visibility = Visibility.Visible;
        EmptyState.Visibility = Visibility.Collapsed;
        RepositoryEmptyState.Visibility = Visibility.Collapsed;
    }

    private void HideError()
    {
        ErrorMessage.Text = string.Empty;
        ErrorState.Visibility = Visibility.Collapsed;
    }

    private string? SelectedOrganization => (OrganizationCombo.SelectedItem as GitHubOrganizationOption)?.Login;

    private static string ActivityStatus(string noun, int count, int scanned, int skipped) =>
        $"{count:N0} {(count == 1 ? noun : $"{noun}s")} from {scanned:N0} repos{(skipped == 0 ? string.Empty : $", skipped {skipped:N0}")}";

    private static void Replace<T>(ObservableCollection<T> target, IEnumerable<T> items)
    {
        target.Clear();
        foreach (var item in items)
        {
            target.Add(item);
        }
    }

    private static void CopyText(string text)
    {
        var package = new DataPackage();
        package.SetText(text);
        Clipboard.SetContent(package);
    }

    private static T? FindDescendant<T>(DependencyObject root) where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is T match)
            {
                return match;
            }

            if (FindDescendant<T>(child) is { } descendant)
            {
                return descendant;
            }
        }

        return null;
    }

    private enum ActivityMode
    {
        ReviewRequests,
        Created,
        Releases,
        WorkflowRuns
    }
}

public enum GitHubPageMode
{
    PullRequests,
    Releases,
    WorkflowRuns
}

public sealed record GitHubOrganizationOption(string DisplayName, string? Login, string AvatarUrl)
{
    public ImageSource? AvatarSource =>
        Uri.TryCreate(AvatarUrl, UriKind.Absolute, out var uri) ? new BitmapImage(uri) : null;
}
