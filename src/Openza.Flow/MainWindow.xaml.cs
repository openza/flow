using System.Collections.ObjectModel;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Openza.Flow.Core.Models;
using Openza.Flow.Core.Services;
using Openza.Flow.Services;
using Openza.Flow.ViewModels;
using Windows.ApplicationModel.DataTransfer;

namespace Openza.Flow;

public sealed partial class MainWindow : Window
{
    private readonly ITokenStore _tokenStore;
    private readonly GitHubAuthService _authService;
    private readonly GitHubPullRequestService _pullRequestService;
    private readonly GitHubRepositoryActivityService _repositoryActivityService;
    private readonly IFlowCacheStore _cacheStore;
    private readonly AppSettingsService _settings;
    private readonly BackgroundRefreshService _backgroundRefresh;
    private readonly TrayIconService _trayIcon;
    private readonly FlowNotificationService _notifications;
    private readonly ObservableCollection<PrListItem> _pullRequests = [];
    private readonly ObservableCollection<PrListItem> _reviewedPullRequests = [];
    private readonly ObservableCollection<PrListItem> _recentlyCreatedPullRequests = [];
    private readonly ObservableCollection<RepositoryActivityListItem> _repositoryActivityItems = [];
    private IReadOnlyList<GithubRelease> _loadedReleases = [];
    private IReadOnlyList<GithubWorkflowRun> _loadedWorkflowRuns = [];
    private readonly DispatcherQueueTimer _searchTimer;
    private readonly DispatcherQueueTimer _autoRefreshTimer;

    private PageMode _pageMode = PageMode.ReviewRequests;
    private CancellationTokenSource? _loadCts;
    private CancellationTokenSource? _authCts;
    private DeviceCodeInfo? _currentDeviceCode;
    private int _loadGeneration;
    private string? _endCursor;
    private bool _hasNextPage;
    private bool _isInitialized;
    private bool _isLoadingSettings;
    private bool _isLoadingMore;
    private bool _isExiting;

    public MainWindow(
        ITokenStore tokenStore,
        GitHubAuthService authService,
        GitHubPullRequestService pullRequestService,
        GitHubRepositoryActivityService repositoryActivityService,
        IFlowCacheStore cacheStore,
        AppSettingsService settings,
        BackgroundRefreshService backgroundRefresh,
        TrayIconService trayIcon,
        FlowNotificationService notifications)
    {
        _tokenStore = tokenStore;
        _authService = authService;
        _pullRequestService = pullRequestService;
        _repositoryActivityService = repositoryActivityService;
        _cacheStore = cacheStore;
        _settings = settings;
        _backgroundRefresh = backgroundRefresh;
        _trayIcon = trayIcon;
        _notifications = notifications;

        InitializeComponent();
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        Title = "Openza Flow";

        try
        {
            SystemBackdrop = new MicaBackdrop();
        }
        catch (Exception exception)
        {
            AppLog.Write(exception);
        }

        PullRequestList.ItemsSource = _pullRequests;
        ReviewedList.ItemsSource = _reviewedPullRequests;
        RecentlyCreatedList.ItemsSource = _recentlyCreatedPullRequests;
        RepositoryActivityList.ItemsSource = _repositoryActivityItems;

        _searchTimer = DispatcherQueue.CreateTimer();
        _searchTimer.Interval = TimeSpan.FromMilliseconds(500);
        _searchTimer.Tick += async (_, _) =>
        {
            _searchTimer.Stop();
            await RefreshAsync();
        };

        _autoRefreshTimer = DispatcherQueue.CreateTimer();
        _autoRefreshTimer.Interval = TimeSpan.FromMinutes(5);
        _autoRefreshTimer.Tick += async (_, _) => await RefreshVisibleDashboardAsync();

        var appWindow = WindowInterop.GetAppWindow(this);
        appWindow.Closing += OnAppWindowClosing;
        Closed += (_, _) =>
        {
            _autoRefreshTimer.Stop();
            _authCts?.Cancel();
            _authCts?.Dispose();
            _trayIcon.Dispose();
        };

        Activated += async (_, _) =>
        {
            if (!_isInitialized)
            {
                _isInitialized = true;
                await InitializeAsync();
            }
        };
    }

    public async Task RefreshAsync()
    {
        if (DashboardView.Visibility != Visibility.Visible || _pageMode == PageMode.Settings)
        {
            return;
        }

        if (_pageMode is PageMode.Releases or PageMode.Actions)
        {
            await LoadRepositoryActivityAsync();
            return;
        }

        await LoadPrimaryListAsync(useCacheFirst: false);
        await LoadActivityListsAsync(_loadGeneration, SelectedOrganization, _loadCts?.Token ?? CancellationToken.None);
    }

    private async Task RefreshVisibleDashboardAsync()
    {
        if (DashboardView.Visibility != Visibility.Visible || _pageMode == PageMode.Settings)
        {
            return;
        }

        if (_settings.NotificationsEnabled)
        {
            try
            {
                await _backgroundRefresh.RefreshOnceAsync();
            }
            catch (Exception exception)
            {
                AppLog.Write(exception);
            }
        }

        await RefreshAsync();
    }

    public void ShowWindow()
    {
        WindowInterop.Show(this);
    }

    public void ExitApplication()
    {
        _isExiting = true;
        _trayIcon.Dispose();
        Close();
    }

    private async Task InitializeAsync()
    {
        ApplyTheme();
        await InitializeSettingsAsync();
        if (!await _authService.EnsureStoredCredentialsAsync())
        {
            ShowAuth();
            return;
        }

        await ShowDashboardAsync();
    }

    private async Task InitializeSettingsAsync()
    {
        _isLoadingSettings = true;
        NotificationsToggle.IsOn = _settings.NotificationsEnabled;
        RunInBackgroundToggle.IsOn = _settings.RunInBackground;
        StartWithWindowsToggle.IsOn = await _settings.GetStartWithWindowsAsync();
        ThemeCombo.SelectedIndex = _settings.Theme switch
        {
            "light" => 1,
            "dark" => 2,
            _ => 0
        };
        _isLoadingSettings = false;
    }

    private void ShowAuth()
    {
        AuthView.Visibility = Visibility.Visible;
        DashboardView.Visibility = Visibility.Collapsed;
    }

    private async Task ShowDashboardAsync()
    {
        AuthView.Visibility = Visibility.Collapsed;
        DashboardView.Visibility = Visibility.Visible;
        DashboardView.SelectedItem = ReviewRequestsNavItem;
        _pageMode = PageMode.ReviewRequests;
        await LoadOrganizationsAsync();
        await UpdateUserMenuAsync();
        await LoadPrimaryListAsync(useCacheFirst: true);
        await LoadActivityListsAsync(_loadGeneration, SelectedOrganization, _loadCts?.Token ?? CancellationToken.None);
        _autoRefreshTimer.Start();
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

        var selected = _settings.SelectedOrganization;
        var orgItems = new List<OrgFilterItem> { new("All organizations", null, string.Empty) };
        orgItems.AddRange(organizations.Select(org => new OrgFilterItem(
            string.IsNullOrWhiteSpace(org.Name) ? org.Login : org.Name,
            org.Login,
            org.AvatarUrl)));
        OrganizationCombo.ItemsSource = orgItems;
        OrganizationCombo.SelectedItem = orgItems.FirstOrDefault(item => item.Login == selected) ?? orgItems[0];
    }

    private async Task UpdateUserMenuAsync()
    {
        var username = await _tokenStore.GetUsernameAsync();
        var displayName = string.IsNullOrWhiteSpace(username) ? "User" : username;
        UserMenuButton.Content = displayName;
        AccountNameText.Text = string.IsNullOrWhiteSpace(username) ? "GitHub account" : username;
    }

    private async Task LoadPrimaryListAsync(bool useCacheFirst)
    {
        _loadCts?.Cancel();
        _loadCts = new CancellationTokenSource();
        var cancellationToken = _loadCts.Token;
        var loadGeneration = ++_loadGeneration;

        SetLoading(true);
        try
        {
            var query = SearchBox.Text?.Trim() ?? string.Empty;
            var organization = SelectedOrganization;
            _endCursor = null;
            _hasNextPage = false;
            HidePrimaryError();

            if (useCacheFirst && string.IsNullOrWhiteSpace(query) && organization is null)
            {
                await LoadCachedPrimaryListAsync();
            }

            PaginatedResult<PullRequest> result;
            if (!string.IsNullOrWhiteSpace(query))
            {
                if (_pageMode == PageMode.Created)
                {
                    PageTitle.Text = "Search Created Pull Requests";
                    result = await _pullRequestService.SearchCreatedPullRequestsAsync(query, organization: organization, cancellationToken: cancellationToken);
                }
                else
                {
                    PageTitle.Text = "Search Review Requests";
                    result = await _pullRequestService.SearchReviewRequestsAsync(query, organization: organization, cancellationToken: cancellationToken);
                }
            }
            else if (_pageMode == PageMode.Created)
            {
                PageTitle.Text = "Created Pull Requests";
                result = await _pullRequestService.GetCreatedPullRequestsAsync(organization: organization, cancellationToken: cancellationToken);
                if (string.IsNullOrWhiteSpace(query) && organization is null)
                {
                    await _cacheStore.SetAsync(FlowCacheKeys.CreatedPullRequests, result.Items, cancellationToken);
                }
            }
            else
            {
                PageTitle.Text = "Review Requests";
                result = await _pullRequestService.GetReviewRequestsAsync(organization: organization, cancellationToken: cancellationToken);
                if (organization is null)
                {
                    await _cacheStore.SetAsync(FlowCacheKeys.ReviewRequests, result.Items, cancellationToken);
                }
            }

            if (cancellationToken.IsCancellationRequested || loadGeneration != _loadGeneration)
            {
                return;
            }

            _endCursor = result.EndCursor;
            _hasNextPage = result.HasNextPage;
            ReplaceList(_pullRequests, result.Items.Select(pr => new PrListItem(pr)));
            UpdatePrimaryEmptyState();
            StatusText.Text = $"{result.Items.Count} pull request{(result.Items.Count == 1 ? string.Empty : "s")}";
            LoadMoreButton.IsEnabled = _hasNextPage;
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            StatusText.Text = exception.Message;
            ShowPrimaryError(exception.Message);
            AppLog.Write(exception);
        }
        finally
        {
            if (loadGeneration == _loadGeneration)
            {
                SetLoading(false);
            }
        }
    }

    private async Task LoadCachedPrimaryListAsync()
    {
        var cacheKey = _pageMode == PageMode.Created
            ? FlowCacheKeys.CreatedPullRequests
            : FlowCacheKeys.ReviewRequests;
        var cached = await _cacheStore.GetAsync<List<PullRequest>>(cacheKey);
        if (cached is { Count: > 0 })
        {
            ReplaceList(_pullRequests, cached.Select(pr => new PrListItem(pr)));
            UpdatePrimaryEmptyState();
            StatusText.Text = $"Loaded {cached.Count} cached pull request{(cached.Count == 1 ? string.Empty : "s")}";
        }
    }

    private async Task LoadActivityListsAsync(int loadGeneration, string? organization, CancellationToken cancellationToken)
    {
        try
        {
            if (organization is null)
            {
                var cachedReviewed = await _cacheStore.GetAsync<List<ReviewedPullRequest>>(FlowCacheKeys.ReviewedPullRequests, cancellationToken);
                if (cachedReviewed is { Count: > 0 })
                {
                    var sortedCached = cachedReviewed.OrderByDescending(pr => pr.ReviewedAt).ToList();
                    if (!IsActivityLoadCurrent(loadGeneration, organization, cancellationToken))
                    {
                        return;
                    }

                    ReplaceList(_reviewedPullRequests, sortedCached.Select(pr => new PrListItem(pr)));
                }
            }

            var reviewedResult = await _pullRequestService.GetReviewedPullRequestsAsync(organization: organization, cancellationToken: cancellationToken);
            var reviewed = reviewedResult.Items
                .OrderByDescending(pr => pr.ReviewedAt)
                .ToList();
            if (!IsActivityLoadCurrent(loadGeneration, organization, cancellationToken))
            {
                return;
            }

            ReplaceList(_reviewedPullRequests, reviewed.Select(pr => new PrListItem(pr)));
            if (organization is null)
            {
                await _cacheStore.SetAsync(FlowCacheKeys.ReviewedPullRequests, reviewed, cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception exception)
        {
            AppLog.Write(exception);
        }

        try
        {
            if (organization is null)
            {
                var cachedCreated = await _cacheStore.GetAsync<List<CreatedPullRequest>>(FlowCacheKeys.RecentlyCreatedPullRequests, cancellationToken);
                if (cachedCreated is { Count: > 0 })
                {
                    var sortedCached = cachedCreated.OrderByDescending(pr => pr.CreatedAt).ToList();
                    if (!IsActivityLoadCurrent(loadGeneration, organization, cancellationToken))
                    {
                        return;
                    }

                    ReplaceList(_recentlyCreatedPullRequests, sortedCached.Select(pr => new PrListItem(pr)));
                }
            }

            var createdResult = await _pullRequestService.GetRecentlyCreatedPullRequestsAsync(organization, cancellationToken);
            var created = createdResult
                .OrderByDescending(pr => pr.CreatedAt)
                .ToList();
            if (!IsActivityLoadCurrent(loadGeneration, organization, cancellationToken))
            {
                return;
            }

            ReplaceList(_recentlyCreatedPullRequests, created.Select(pr => new PrListItem(pr)));
            if (organization is null)
            {
                await _cacheStore.SetAsync(FlowCacheKeys.RecentlyCreatedPullRequests, created, cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            AppLog.Write(exception);
        }
    }

    private bool IsActivityLoadCurrent(int loadGeneration, string? organization, CancellationToken cancellationToken)
    {
        return !cancellationToken.IsCancellationRequested
            && loadGeneration == _loadGeneration
            && organization == SelectedOrganization;
    }

    private async Task LoadMoreAsync()
    {
        if (_isLoadingMore || !_hasNextPage || string.IsNullOrWhiteSpace(_endCursor))
        {
            return;
        }

        _isLoadingMore = true;
        var loadGeneration = _loadGeneration;
        var pageMode = _pageMode;
        var cursor = _endCursor;
        var organization = SelectedOrganization;
        var query = SearchBox.Text?.Trim() ?? string.Empty;
        var cancellationToken = _loadCts?.Token ?? CancellationToken.None;

        SetLoading(true);
        try
        {
            PaginatedResult<PullRequest> result;
            if (!string.IsNullOrWhiteSpace(query))
            {
                result = pageMode == PageMode.Created
                    ? await _pullRequestService.SearchCreatedPullRequestsAsync(query, cursor, organization, cancellationToken)
                    : await _pullRequestService.SearchReviewRequestsAsync(query, cursor, organization, cancellationToken);
            }
            else if (pageMode == PageMode.Created)
            {
                result = await _pullRequestService.GetCreatedPullRequestsAsync(cursor, organization, cancellationToken);
            }
            else
            {
                result = await _pullRequestService.GetReviewRequestsAsync(cursor, organization, cancellationToken);
            }

            if (cancellationToken.IsCancellationRequested ||
                loadGeneration != _loadGeneration ||
                pageMode != _pageMode ||
                cursor != _endCursor ||
                organization != SelectedOrganization ||
                query != (SearchBox.Text?.Trim() ?? string.Empty))
            {
                return;
            }

            _endCursor = result.EndCursor;
            _hasNextPage = result.HasNextPage;
            foreach (var item in result.Items.Select(pr => new PrListItem(pr)))
            {
                _pullRequests.Add(item);
            }

            UpdatePrimaryEmptyState();
            LoadMoreButton.IsEnabled = _hasNextPage;
            StatusText.Text = $"{_pullRequests.Count} pull requests";
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            StatusText.Text = exception.Message;
            AppLog.Write(exception);
        }
        finally
        {
            _isLoadingMore = false;
            if (loadGeneration == _loadGeneration)
            {
                SetLoading(false);
            }
        }
    }

    private async Task LoadRepositoryActivityAsync()
    {
        _loadCts?.Cancel();
        _loadCts = new CancellationTokenSource();
        var cancellationToken = _loadCts.Token;
        var loadGeneration = ++_loadGeneration;
        var organization = SelectedOrganization;
        var query = SearchBox.Text?.Trim() ?? string.Empty;

        SetRepositoryActivityLoading(true);
        HideRepositoryActivityError();
        try
        {
            if (string.IsNullOrWhiteSpace(organization))
            {
                _loadedReleases = [];
                _loadedWorkflowRuns = [];
                ReplaceList(_repositoryActivityItems, []);
                ShowRepositoryActivityEmpty("Choose an organization", "Releases and Actions are scoped to one selected organization.");
                RepositoryActivityStatusText.Text = "Choose an organization";
                return;
            }

            if (_pageMode == PageMode.Releases)
            {
                RepositoryActivityTitle.Text = "Releases";
                RepositoryActivitySubtitle.Text = $"Recent releases across repositories in {organization}.";
                var result = await _repositoryActivityService.GetRecentReleasesAsync(organization, cancellationToken);
                if (!IsRepositoryActivityLoadCurrent(loadGeneration, organization, cancellationToken))
                {
                    return;
                }

                _loadedReleases = result.Items;
                ApplyReleaseFilter(query);
                RepositoryActivityStatusText.Text = ActivityStatus("release", _repositoryActivityItems.Count, result.ScannedRepositoryCount, result.SkippedRepositoryCount);
            }
            else if (_pageMode == PageMode.Actions)
            {
                RepositoryActivityTitle.Text = "Actions";
                RepositoryActivitySubtitle.Text = $"Recent workflow runs across repositories in {organization}.";
                var result = await _repositoryActivityService.GetRecentWorkflowRunsAsync(organization, cancellationToken);
                if (!IsRepositoryActivityLoadCurrent(loadGeneration, organization, cancellationToken))
                {
                    return;
                }

                _loadedWorkflowRuns = result.Items;
                ApplyWorkflowRunFilter(query);
                RepositoryActivityStatusText.Text = ActivityStatus("workflow run", _repositoryActivityItems.Count, result.ScannedRepositoryCount, result.SkippedRepositoryCount);
            }

            UpdateRepositoryActivityEmptyState();
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            RepositoryActivityStatusText.Text = exception.Message;
            ShowRepositoryActivityError(exception.Message);
            AppLog.Write(exception);
        }
        finally
        {
            if (loadGeneration == _loadGeneration)
            {
                SetRepositoryActivityLoading(false);
            }
        }
    }

    private void ApplyRepositoryActivityFilter()
    {
        var query = SearchBox.Text?.Trim() ?? string.Empty;
        if (_pageMode == PageMode.Releases)
        {
            ApplyReleaseFilter(query);
        }
        else if (_pageMode == PageMode.Actions)
        {
            ApplyWorkflowRunFilter(query);
        }

        UpdateRepositoryActivityEmptyState();
    }

    private void ApplyReleaseFilter(string query)
    {
        ReplaceList(
            _repositoryActivityItems,
            _loadedReleases
                .Where(release => RepositoryActivitySearch.MatchesRelease(release, query))
                .Select(release => new RepositoryActivityListItem(release)));
    }

    private void ApplyWorkflowRunFilter(string query)
    {
        ReplaceList(
            _repositoryActivityItems,
            _loadedWorkflowRuns
                .Where(run => RepositoryActivitySearch.MatchesWorkflowRun(run, query))
                .Select(run => new RepositoryActivityListItem(run)));
    }

    private bool IsRepositoryActivityLoadCurrent(int loadGeneration, string organization, CancellationToken cancellationToken)
    {
        return !cancellationToken.IsCancellationRequested
            && loadGeneration == _loadGeneration
            && organization == SelectedOrganization;
    }

    private static string ActivityStatus(string noun, int visibleCount, int scannedCount, int skippedCount)
    {
        var plural = visibleCount == 1 ? noun : $"{noun}s";
        var skipped = skippedCount == 0 ? string.Empty : $", skipped {skippedCount}";
        return $"{visibleCount} {plural} from {scannedCount} repos{skipped}";
    }

    private string? SelectedOrganization => OrganizationCombo.SelectedItem is OrgFilterItem item ? item.Login : null;

    private void SetLoading(bool isLoading)
    {
        LoadingRing.IsActive = isLoading;
        SearchBox.IsEnabled = !isLoading;
        OrganizationCombo.IsEnabled = !isLoading;
        LoadMoreButton.IsEnabled = !isLoading && _hasNextPage;
        UpdatePrimaryEmptyState(isLoading);
    }

    private void SetRepositoryActivityLoading(bool isLoading)
    {
        RepositoryActivityLoadingRing.IsActive = isLoading;
        SearchBox.IsEnabled = !isLoading;
        OrganizationCombo.IsEnabled = !isLoading;
        UpdateRepositoryActivityEmptyState(isLoading);
    }

    private void UpdatePrimaryEmptyState(bool isLoading = false)
    {
        var showEmptyState = PrimaryErrorState.Visibility != Visibility.Visible
            && !isLoading
            && DashboardView.Visibility == Visibility.Visible
            && _pageMode != PageMode.Settings
            && _pullRequests.Count == 0;
        PrimaryEmptyState.Visibility = showEmptyState ? Visibility.Visible : Visibility.Collapsed;
    }

    private void UpdateRepositoryActivityEmptyState(bool isLoading = false)
    {
        if (RepositoryActivityContent.Visibility != Visibility.Visible)
        {
            return;
        }

        var showEmptyState = RepositoryActivityErrorState.Visibility != Visibility.Visible
            && !isLoading
            && _repositoryActivityItems.Count == 0;
        RepositoryActivityEmptyState.Visibility = showEmptyState ? Visibility.Visible : Visibility.Collapsed;
        if (showEmptyState && !string.IsNullOrWhiteSpace(SelectedOrganization))
        {
            RepositoryActivityEmptyTitle.Text = _pageMode == PageMode.Releases ? "No releases found" : "No workflow runs found";
            RepositoryActivityEmptyMessage.Text = "No matching items were found for this organization and search.";
        }
    }

    private void ShowRepositoryActivityEmpty(string title, string message)
    {
        RepositoryActivityEmptyTitle.Text = title;
        RepositoryActivityEmptyMessage.Text = message;
        RepositoryActivityEmptyState.Visibility = Visibility.Visible;
        RepositoryActivityErrorState.Visibility = Visibility.Collapsed;
    }

    private void ShowPrimaryError(string message)
    {
        PrimaryErrorMessage.Text = message;
        PrimaryErrorState.Visibility = Visibility.Visible;
        PrimaryEmptyState.Visibility = Visibility.Collapsed;
    }

    private void ShowRepositoryActivityError(string message)
    {
        RepositoryActivityErrorMessage.Text = message;
        RepositoryActivityErrorState.Visibility = Visibility.Visible;
        RepositoryActivityEmptyState.Visibility = Visibility.Collapsed;
    }

    private void HidePrimaryError()
    {
        PrimaryErrorState.Visibility = Visibility.Collapsed;
        PrimaryErrorMessage.Text = string.Empty;
    }

    private void HideRepositoryActivityError()
    {
        RepositoryActivityErrorState.Visibility = Visibility.Collapsed;
        RepositoryActivityErrorMessage.Text = string.Empty;
    }

    private static void ReplaceList<T>(ObservableCollection<T> target, IEnumerable<T> items)
    {
        target.Clear();
        foreach (var item in items)
        {
            target.Add(item);
        }
    }

    private async void OnGitHubOAuthClicked(object sender, RoutedEventArgs e)
    {
        _authCts?.Cancel();
        _authCts?.Dispose();
        _authCts = new CancellationTokenSource();
        DeviceCodePanel.Visibility = Visibility.Collapsed;
        AuthInfoBar.IsOpen = true;
        AuthInfoBar.Severity = InfoBarSeverity.Informational;
        AuthInfoBar.Message = "Requesting a GitHub device code...";

        try
        {
            var code = await _authService.RequestDeviceCodeAsync(_authCts.Token);
            _currentDeviceCode = code;
            DeviceCodeBox.Text = code.UserCode;
            DeviceCodePanel.Visibility = Visibility.Visible;
            AuthInfoBar.Message = "Waiting for GitHub authorization...";
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
            await ShowDashboardAsync();
        }
        catch (OperationCanceledException)
        {
            AuthInfoBar.Severity = InfoBarSeverity.Informational;
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
        AuthInfoBar.Message = "Validating token...";
        var validation = await _authService.ValidateAndSaveTokenAsync(token);
        if (!validation.IsValid)
        {
            AuthInfoBar.Severity = InfoBarSeverity.Error;
            AuthInfoBar.Message = validation.ErrorMessage ?? "Token validation failed.";
            return;
        }

        PatBox.Password = string.Empty;
        await ShowDashboardAsync();
    }

    private void OnCancelDeviceFlowClicked(object sender, RoutedEventArgs e)
    {
        _authCts?.Cancel();
        DeviceCodePanel.Visibility = Visibility.Collapsed;
    }

    private async void OnCopyDeviceCodeClicked(object sender, RoutedEventArgs e)
    {
        CopyText(DeviceCodeBox.Text);
        AuthInfoBar.IsOpen = true;
        AuthInfoBar.Severity = InfoBarSeverity.Success;
        AuthInfoBar.Message = "Device code copied.";
        await Task.Delay(TimeSpan.FromSeconds(2));
        if (AuthView.Visibility == Visibility.Visible && AuthInfoBar.Severity == InfoBarSeverity.Success)
        {
            AuthInfoBar.IsOpen = false;
        }
    }

    private async void OnOpenDevicePageClicked(object sender, RoutedEventArgs e)
    {
        if (_currentDeviceCode is not null)
        {
            await Windows.System.Launcher.LaunchUriAsync(new Uri(_currentDeviceCode.VerificationUri));
        }
    }

    private async void OnCreateTokenClicked(object sender, RoutedEventArgs e)
    {
        await Windows.System.Launcher.LaunchUriAsync(new Uri("https://github.com/settings/tokens/new?description=Openza%20Flow&scopes=repo,read:user,read:org"));
    }

    private async void OnRefreshClicked(object sender, RoutedEventArgs e)
    {
        await RefreshAsync();
    }

    private async void OnRefreshTapped(object sender, TappedRoutedEventArgs e)
    {
        e.Handled = true;
        await RefreshAsync();
    }

    private async void OnLoadMoreClicked(object sender, RoutedEventArgs e)
    {
        await LoadMoreAsync();
    }

    private async void OnPrOpenRequested(object? sender, PrListItem item)
    {
        await OpenPullRequestAsync(item);
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

    private async Task OpenPullRequestAsync(PrListItem item)
    {
        if (Uri.TryCreate(item.HtmlUrl, UriKind.Absolute, out var uri))
        {
            await Windows.System.Launcher.LaunchUriAsync(uri);
        }
    }

    private async void OnNavigationSelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.IsSettingsSelected)
        {
            _pageMode = PageMode.Settings;
            MainContent.Visibility = Visibility.Collapsed;
            RepositoryActivityContent.Visibility = Visibility.Collapsed;
            SettingsContent.Visibility = Visibility.Visible;
            return;
        }

        if (args.SelectedItem is not NavigationViewItem item)
        {
            return;
        }

        _pageMode = (item.Tag as string) switch
        {
            "created" => PageMode.Created,
            "releases" => PageMode.Releases,
            "actions" => PageMode.Actions,
            _ => PageMode.ReviewRequests
        };
        MainContent.Visibility = _pageMode is PageMode.ReviewRequests or PageMode.Created ? Visibility.Visible : Visibility.Collapsed;
        RepositoryActivityContent.Visibility = _pageMode is PageMode.Releases or PageMode.Actions ? Visibility.Visible : Visibility.Collapsed;
        SettingsContent.Visibility = Visibility.Collapsed;
        SearchBox.PlaceholderText = _pageMode switch
        {
            PageMode.Releases => "Search releases",
            PageMode.Actions => "Search workflow runs",
            _ => "Search pull requests"
        };

        if (_pageMode is PageMode.Releases or PageMode.Actions)
        {
            await LoadRepositoryActivityAsync();
        }
        else
        {
            await LoadPrimaryListAsync(useCacheFirst: true);
        }
    }

    private void OnSearchTextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (args.Reason != AutoSuggestionBoxTextChangeReason.UserInput)
        {
            return;
        }

        if (_pageMode is PageMode.Releases or PageMode.Actions)
        {
            ApplyRepositoryActivityFilter();
            return;
        }

        _searchTimer.Stop();
        _searchTimer.Start();
    }

    private async void OnOrganizationChanged(object sender, SelectionChangedEventArgs e)
    {
        if (OrganizationCombo.SelectedItem is OrgFilterItem item)
        {
            _settings.SelectedOrganization = item.Login;
            await RefreshAsync();
        }
    }

    private async void OnSignOutClicked(object sender, RoutedEventArgs e)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = (Content as FrameworkElement)?.XamlRoot,
            Title = "Sign out",
            Content = "Are you sure you want to sign out of GitHub?",
            PrimaryButtonText = "Sign out",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        _autoRefreshTimer.Stop();
        await _backgroundRefresh.StopAsync();
        await _authService.SignOutAsync();
        await _cacheStore.ClearAsync();
        _pullRequests.Clear();
        _reviewedPullRequests.Clear();
        _recentlyCreatedPullRequests.Clear();
        _repositoryActivityItems.Clear();
        _loadedReleases = [];
        _loadedWorkflowRuns = [];
        ShowAuth();
    }

    private void OnPullRequestListLoaded(object sender, RoutedEventArgs e)
    {
        if (FindDescendant<ScrollViewer>(PullRequestList) is { } scrollViewer)
        {
            scrollViewer.ViewChanged += async (_, args) =>
            {
                if (args.IsIntermediate ||
                    !_hasNextPage ||
                    _isLoadingMore ||
                    string.IsNullOrWhiteSpace(_endCursor) ||
                    scrollViewer.ScrollableHeight <= 0)
                {
                    return;
                }

                if (scrollViewer.VerticalOffset >= scrollViewer.ScrollableHeight - 240)
                {
                    await LoadMoreAsync();
                }
            };
        }
    }

    private void OnNotificationsToggled(object sender, RoutedEventArgs e)
    {
        if (_isLoadingSettings)
        {
            return;
        }

        _settings.NotificationsEnabled = NotificationsToggle.IsOn;
        if (!_settings.NotificationsEnabled)
        {
            SettingsInfoBar.IsOpen = false;
            return;
        }

        if (!_notifications.CanShowNotifications(out var message))
        {
            ShowSettingsMessage(message, InfoBarSeverity.Warning);
            return;
        }

        if (!_settings.RunInBackground)
        {
            ShowSettingsMessage("Notifications are on. Turn on Run in background to get alerts while Flow is closed.", InfoBarSeverity.Informational);
        }
    }

    private void OnTestNotificationClicked(object sender, RoutedEventArgs e)
    {
        if (!_settings.NotificationsEnabled)
        {
            ShowSettingsMessage("Turn on notifications first.", InfoBarSeverity.Warning);
            return;
        }

        var sent = _notifications.ShowTestNotification(out var message);
        ShowSettingsMessage(message, sent ? InfoBarSeverity.Success : InfoBarSeverity.Warning);
    }

    private async void OnRunInBackgroundToggled(object sender, RoutedEventArgs e)
    {
        if (_isLoadingSettings)
        {
            return;
        }

        _settings.RunInBackground = RunInBackgroundToggle.IsOn;
        if (_settings.RunInBackground)
        {
            _trayIcon.SetVisible(true);
            _backgroundRefresh.Start();
        }
        else
        {
            _trayIcon.SetVisible(false);
            await _backgroundRefresh.StopAsync();
        }
    }

    private async void OnStartWithWindowsToggled(object sender, RoutedEventArgs e)
    {
        if (_isLoadingSettings)
        {
            return;
        }

        _isLoadingSettings = true;
        StartWithWindowsToggle.IsOn = await _settings.SetStartWithWindowsAsync(StartWithWindowsToggle.IsOn);
        _isLoadingSettings = false;
    }

    private void OnThemeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isLoadingSettings || ThemeCombo.SelectedItem is not ComboBoxItem item || item.Tag is not string theme)
        {
            return;
        }

        _settings.Theme = theme;
        ApplyTheme();
    }

    private void ApplyTheme()
    {
        if (Content is not FrameworkElement root)
        {
            return;
        }

        root.RequestedTheme = _settings.Theme switch
        {
            "light" => ElementTheme.Light,
            "dark" => ElementTheme.Dark,
            _ => ElementTheme.Default
        };
    }

    private void ShowSettingsMessage(string message, InfoBarSeverity severity)
    {
        SettingsInfoBar.Severity = severity;
        SettingsInfoBar.Message = message;
        SettingsInfoBar.IsOpen = true;
    }

    private static void CopyText(string text)
    {
        var package = new DataPackage();
        package.SetText(text);
        Clipboard.SetContent(package);
    }

    private static T? FindDescendant<T>(DependencyObject root)
        where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match)
            {
                return match;
            }

            var descendant = FindDescendant<T>(child);
            if (descendant is not null)
            {
                return descendant;
            }
        }

        return null;
    }

    private void OnAppWindowClosing(Microsoft.UI.Windowing.AppWindow sender, Microsoft.UI.Windowing.AppWindowClosingEventArgs args)
    {
        if (_isExiting || !_settings.RunInBackground)
        {
            return;
        }

        args.Cancel = true;
        WindowInterop.Hide(this);
        _trayIcon.SetVisible(true);
        _trayIcon.ShowBackgroundHint();
    }

    private sealed record OrgFilterItem(string DisplayName, string? Login, string AvatarUrl)
    {
        public override string ToString() => DisplayName;
    }

    private enum PageMode
    {
        ReviewRequests,
        Created,
        Releases,
        Actions,
        Settings
    }
}
