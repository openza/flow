using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Openza.Flow.Core.Models;
using Openza.Flow.Core.Services;
using Openza.Flow.Pages;
using Openza.Flow.Services;

namespace Openza.Flow;

public sealed partial class MainWindow : Window
{
    private readonly AppSettingsService _settings;
    private readonly BackgroundRefreshService _backgroundRefresh;
    private readonly TrayIconService _tray;
    private readonly IAgentSessionWorkspace _sessionWorkspace;
    private readonly HomePage _homePage;
    private readonly AgentSessionsPage _sessionsPage;
    private readonly ActivityPage _activityPage;
    private readonly SettingsPage _settingsPage;
    private ShellPage _currentPage = ShellPage.Home;
    private bool _initialized;
    private bool _navigating;
    private bool _syncingGitHubContext;
    private bool _isExiting;
    private int _navigationGeneration;

    public MainWindow(
        ITokenStore tokenStore,
        GitHubAuthService authService,
        GitHubPullRequestService pullRequestService,
        GitHubRepositoryActivityService repositoryActivityService,
        IFlowCacheStore cacheStore,
        AppSettingsService settings,
        BackgroundRefreshService backgroundRefresh,
        TrayIconService tray,
        FlowNotificationService notifications,
        IAgentSessionWorkspace sessionWorkspace,
        ITerminalLauncher terminalLauncher)
    {
        _settings = settings;
        _backgroundRefresh = backgroundRefresh;
        _tray = tray;
        _sessionWorkspace = sessionWorkspace;

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

        var githubState = new GitHubWorkspaceState();
        _homePage = new HomePage(sessionWorkspace, githubState, terminalLauncher, settings);
        _sessionsPage = new AgentSessionsPage(sessionWorkspace, settings, terminalLauncher);
        _activityPage = new ActivityPage(
            tokenStore,
            authService,
            pullRequestService,
            repositoryActivityService,
            cacheStore,
            settings,
            backgroundRefresh,
            githubState);
        _settingsPage = new SettingsPage(settings, sessionWorkspace, backgroundRefresh, tray, notifications, githubState);

        _activityPage.AuthenticationChanged += OnGitHubContextChanged;
        _activityPage.GitHubContextChanged += OnGitHubContextChanged;
        WirePageNavigation();
        _navigating = true;
        ShellNavigation.SelectedItem = HomeNavigationItem;
        PageHost.Content = _homePage;
        _navigating = false;

        var appWindow = WindowInterop.GetAppWindow(this);
        appWindow.Closing += OnAppWindowClosing;
        Closed += async (_, _) =>
        {
            await _activityPage.DisposeAsync();
            await _sessionWorkspace.DisposeAsync();
            _tray.Dispose();
        };
        Activated += OnWindowActivated;
    }

    public void ShowWindow()
    {
        WindowInterop.Show(this);
        _ = ActivateCurrentPageAsync();
    }

    public void ExitApplication()
    {
        _isExiting = true;
        _tray.Dispose();
        Close();
    }

    internal void Maximize()
    {
        try
        {
            var appWindow = WindowInterop.GetAppWindow(this);
            if (appWindow.Presenter is OverlappedPresenter presenter)
            {
                presenter.Maximize();
            }
        }
        catch (Exception exception)
        {
            AppLog.Write(exception);
        }
    }

    public async Task RefreshAsync()
    {
        switch (_currentPage)
        {
            case ShellPage.Home:
                await _homePage.ViewModel.RefreshSessionsAsync();
                await _activityPage.RefreshReviewRequestsForHomeAsync();
                break;
            case ShellPage.Sessions:
                await _sessionsPage.ViewModel.RefreshAsync(preserveExisting: true);
                break;
            case ShellPage.PullRequests:
            case ShellPage.Releases:
            case ShellPage.WorkflowRuns:
                await _activityPage.RefreshAsync();
                break;
        }
    }

    private async void OnWindowActivated(object sender, WindowActivatedEventArgs args)
    {
        if (!_initialized)
        {
            _initialized = true;
            ApplyTheme();
            ShellNavigation.SelectedItem = HomeNavigationItem;
            await NavigateAsync(ShellPage.Home);
            _ = InitializeGitHubAsync();
            return;
        }

        if (args.WindowActivationState != WindowActivationState.Deactivated)
        {
            await ActivateCurrentPageAsync();
        }
    }

    private async Task InitializeGitHubAsync()
    {
        await _activityPage.ActivateAsync();
        _activityPage.SetActive(IsGitHubPage(_currentPage));
        UpdateGitHubContextBar();
    }

    private void WirePageNavigation()
    {
        _homePage.ViewSessionsRequested += (_, _) => SelectNavigation(SessionsNavigationItem);
        _homePage.ViewActivityRequested += (_, _) => SelectNavigation(PullRequestsNavigationItem);
        _homePage.RefreshGitHubRequested += async (_, _) => await _activityPage.RefreshReviewRequestsForHomeAsync();
        _homePage.SessionRequested += async (_, key) =>
        {
            SelectNavigation(SessionsNavigationItem);
            await _sessionsPage.SelectSessionAsync(key);
        };
        _homePage.ProjectRequested += (_, project) =>
        {
            SelectNavigation(SessionsNavigationItem);
            _sessionsPage.FilterToProject(project);
        };
        _settingsPage.ManageGitHubRequested += (_, _) => SelectNavigation(PullRequestsNavigationItem);
        _settingsPage.ThemeChanged += (_, _) => ApplyTheme();
    }

    private void SelectNavigation(NavigationViewItem item)
    {
        if (ReferenceEquals(ShellNavigation.SelectedItem, item))
        {
            return;
        }

        ShellNavigation.SelectedItem = item;
    }

    private async void OnNavigationSelectionChanged(
        NavigationView sender,
        NavigationViewSelectionChangedEventArgs args)
    {
        if (_navigating)
        {
            return;
        }

        if (args.IsSettingsSelected)
        {
            await NavigateAsync(ShellPage.Settings);
            return;
        }

        if (args.SelectedItem is not NavigationViewItem item)
        {
            return;
        }

        await NavigateAsync((item.Tag as string) switch
        {
            "sessions" => ShellPage.Sessions,
            "pull-requests" => ShellPage.PullRequests,
            "releases" => ShellPage.Releases,
            "workflow-runs" => ShellPage.WorkflowRuns,
            _ => ShellPage.Home
        });
    }

    private Task NavigateAsync(ShellPage page)
    {
        if (_navigating)
        {
            return Task.CompletedTask;
        }

        _navigating = true;
        try
        {
            var leavingSessionSurface = _currentPage is ShellPage.Home or ShellPage.Sessions or ShellPage.Settings
                && IsGitHubPage(page);
            _activityPage.SetActive(IsGitHubPage(page));
            _currentPage = page;
            if (IsGitHubPage(page))
            {
                _activityPage.SetPageMode(page switch
                {
                    ShellPage.Releases => GitHubPageMode.Releases,
                    ShellPage.WorkflowRuns => GitHubPageMode.WorkflowRuns,
                    _ => GitHubPageMode.PullRequests
                });
            }

            PageHost.Content = page switch
            {
                ShellPage.Sessions => _sessionsPage,
                ShellPage.PullRequests or ShellPage.Releases or ShellPage.WorkflowRuns => _activityPage,
                ShellPage.Settings => _settingsPage,
                _ => _homePage
            };
            UpdateGitHubContextBar();
            NavigationProgress.Visibility = IsGitHubPage(page) ? Visibility.Collapsed : Visibility.Visible;
            var generation = ++_navigationGeneration;
            _ = CompleteNavigationAsync(page, generation, leavingSessionSurface);
        }
        finally
        {
            _navigating = false;
        }

        return Task.CompletedTask;
    }

    private async Task CompleteNavigationAsync(ShellPage page, int generation, bool leavingSessionSurface)
    {
        try
        {
            await Task.Yield();
            if (generation != _navigationGeneration || page != _currentPage)
            {
                return;
            }

            if (leavingSessionSurface)
            {
                await _sessionsPage.DeactivateAsync();
                await _sessionWorkspace.DeactivateAsync();
                if (generation != _navigationGeneration || page != _currentPage)
                {
                    return;
                }
            }

            await ActivatePageAsync(page);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            AppLog.Write(exception);
        }
        finally
        {
            if (generation == _navigationGeneration)
            {
                NavigationProgress.Visibility = Visibility.Collapsed;
            }
        }
    }

    private Task ActivateCurrentPageAsync() => ActivatePageAsync(_currentPage);

    private Task ActivatePageAsync(ShellPage page) => page switch
    {
        ShellPage.Home => _homePage.ActivateAsync(),
        ShellPage.Sessions => _sessionsPage.ActivateAsync(),
        ShellPage.PullRequests or ShellPage.Releases or ShellPage.WorkflowRuns => _activityPage.ActivateAsync(),
        ShellPage.Settings => _settingsPage.ActivateAsync(),
        _ => Task.CompletedTask
    };

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

    private void OnAppWindowClosing(
        Microsoft.UI.Windowing.AppWindow sender,
        Microsoft.UI.Windowing.AppWindowClosingEventArgs args)
    {
        if (_isExiting || !_settings.RunInBackground)
        {
            return;
        }

        args.Cancel = true;
        _ = _sessionWorkspace.DeactivateAsync();
        _activityPage.SetActive(false);
        WindowInterop.Hide(this);
        _tray.SetVisible(true);
        _tray.ShowBackgroundHint();
    }

    private void OnGitHubContextChanged(object? sender, EventArgs e) => UpdateGitHubContextBar();

    private void UpdateGitHubContextBar()
    {
        var showContext = _currentPage == ShellPage.Home || IsGitHubPage(_currentPage);
        GitHubContextBar.Visibility = showContext ? Visibility.Visible : Visibility.Collapsed;
        if (!showContext)
        {
            return;
        }

        _syncingGitHubContext = true;
        try
        {
            GitHubConnectButton.Visibility = _activityPage.IsAuthenticated ? Visibility.Collapsed : Visibility.Visible;
            ShellOrganizationCombo.Visibility = _activityPage.IsAuthenticated ? Visibility.Visible : Visibility.Collapsed;
            ShellGitHubAccountButton.Visibility = _activityPage.IsAuthenticated ? Visibility.Visible : Visibility.Collapsed;
            ShellGitHubAccountButton.Content = _activityPage.Username;
            ShellOrganizationCombo.ItemsSource = _activityPage.OrganizationOptions;
            ShellOrganizationCombo.SelectedItem = _activityPage.OrganizationOptions.FirstOrDefault(item =>
                string.Equals(item.Login, _activityPage.SelectedOrganizationLogin, StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            _syncingGitHubContext = false;
        }
    }

    private async void OnShellOrganizationChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncingGitHubContext || ShellOrganizationCombo.SelectedItem is not GitHubOrganizationOption option)
        {
            return;
        }

        await _activityPage.SelectOrganizationAsync(option.Login);
    }

    private async void OnShellSignOutClicked(object sender, RoutedEventArgs e) => await _activityPage.SignOutAsync();

    private void OnGitHubConnectClicked(object sender, RoutedEventArgs e) => SelectNavigation(PullRequestsNavigationItem);

    private static bool IsGitHubPage(ShellPage page) =>
        page is ShellPage.PullRequests or ShellPage.Releases or ShellPage.WorkflowRuns;

    private enum ShellPage
    {
        Home,
        Sessions,
        PullRequests,
        Releases,
        WorkflowRuns,
        Settings
    }
}
