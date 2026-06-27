using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Openza.Flow.Core.Services;
using Openza.Flow.Services;
using Windows.Storage;

namespace Openza.Flow;

public partial class App : Application
{
    private readonly HttpClient _httpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(30)
    };

    private MainWindow? _window;
    private TrayIconService? _tray;
    private FlowNotificationService? _notifications;
    private BackgroundRefreshService? _backgroundRefresh;

    public App()
    {
        InitializeComponent();
        DispatcherQueue = DispatcherQueue.GetForCurrentThread();
        UnhandledException += (_, args) => AppLog.Write(args.Exception);
    }

    public DispatcherQueue DispatcherQueue { get; }

    protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
    {
        AppLog.Write("Openza Flow launched.");
        var tokenStore = new WindowsCredentialTokenStore();
        var cachePath = Path.Combine(ApplicationData.Current.LocalFolder.Path, "Cache");
        var cacheStore = new FileFlowCacheStore(cachePath);
        var settings = new AppSettingsService();
        var auth = new GitHubAuthService(_httpClient, tokenStore);
        var pullRequests = new GitHubPullRequestService(_httpClient, tokenStore);
        var repositoryActivity = new GitHubRepositoryActivityService(_httpClient, tokenStore);

        _notifications = new FlowNotificationService();
        _notifications.NotificationActivated += (_, url) =>
        {
            _ = DispatcherQueue.TryEnqueue(() =>
            {
                _window?.ShowWindow();
                LaunchNotificationUrl(url);
            });
        };
        _notifications.Initialize();
        var launchNotificationUrl = _notifications.GetLaunchNotificationUrl();

        _backgroundRefresh = new BackgroundRefreshService(async ct =>
        {
            var result = await pullRequests.GetReviewRequestsAsync(organization: settings.SelectedOrganization, cancellationToken: ct);
            return result.Items;
        });
        _backgroundRefresh.NewReviewRequestsFound += (_, eventArgs) =>
        {
            if (!settings.NotificationsEnabled || eventArgs.PullRequests.Count == 0)
            {
                return;
            }

            _notifications.ShowNewReviewRequests(eventArgs.PullRequests);
        };

        _tray = new TrayIconService("Assets\\app_icon.ico");
        _window = new MainWindow(tokenStore, auth, pullRequests, repositoryActivity, cacheStore, settings, _backgroundRefresh, _tray, _notifications);
        _tray.OpenRequested += (_, _) => _window.ShowWindow();
        _tray.RefreshRequested += (_, _) => _ = DispatcherQueue.TryEnqueue(() => _ = _window.RefreshAsync());
        _tray.ExitRequested += (_, _) => _ = DispatcherQueue.TryEnqueue(() => _window.ExitApplication());

        _window.Activate();
        if (!string.IsNullOrWhiteSpace(launchNotificationUrl))
        {
            _ = DispatcherQueue.TryEnqueue(() =>
            {
                _window.ShowWindow();
                LaunchNotificationUrl(launchNotificationUrl);
            });
        }

        if (settings.RunInBackground)
        {
            _tray.SetVisible(true);
            _backgroundRefresh.Start();
        }
    }

    private static void LaunchNotificationUrl(string? url)
    {
        if (NotificationLaunchUrlValidator.TryCreateGitHubUrl(url, out var uri))
        {
            _ = Windows.System.Launcher.LaunchUriAsync(uri);
        }
    }
}
