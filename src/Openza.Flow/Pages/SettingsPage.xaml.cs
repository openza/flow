using System.Collections.ObjectModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Openza.Flow.Core.Models;
using Openza.Flow.Core.Services;
using Openza.Flow.Services;

namespace Openza.Flow.Pages;

public sealed partial class SettingsPage : Page
{
    private readonly AppSettingsService _settings;
    private readonly IAgentSessionWorkspace _workspace;
    private readonly BackgroundRefreshService _backgroundRefresh;
    private readonly TrayIconService _tray;
    private readonly FlowNotificationService _notifications;
    private readonly GitHubWorkspaceState _github;
    private readonly ObservableCollection<EnvironmentSettingItem> _environments = [];
    private bool _loading;

    public SettingsPage(
        AppSettingsService settings,
        IAgentSessionWorkspace workspace,
        BackgroundRefreshService backgroundRefresh,
        TrayIconService tray,
        FlowNotificationService notifications,
        GitHubWorkspaceState github)
    {
        _settings = settings;
        _workspace = workspace;
        _backgroundRefresh = backgroundRefresh;
        _tray = tray;
        _notifications = notifications;
        _github = github;
        InitializeComponent();
        AgentEnvironmentList.ItemsSource = _environments;
        Loaded += OnPageLoaded;
        Unloaded += OnPageUnloaded;
    }

    public event EventHandler? ManageGitHubRequested;

    public event EventHandler? ThemeChanged;

    private void OnPageLoaded(object sender, RoutedEventArgs e)
    {
        _workspace.SnapshotChanged += OnWorkspaceSnapshotChanged;
        _github.Changed += OnGitHubChanged;
        UpdateEnvironments();
        UpdateConnection();
    }

    private void OnPageUnloaded(object sender, RoutedEventArgs e)
    {
        _workspace.SnapshotChanged -= OnWorkspaceSnapshotChanged;
        _github.Changed -= OnGitHubChanged;
    }

    private void OnWorkspaceSnapshotChanged(object? sender, EventArgs e) => UpdateEnvironments();

    private void OnGitHubChanged(object? sender, EventArgs e) => UpdateConnection();

    public async Task ActivateAsync()
    {
        _loading = true;
        AppVersionText.Text = $"Version {GetPackageVersion()}";
        NotificationsToggle.IsOn = _settings.NotificationsEnabled;
        RunInBackgroundToggle.IsOn = _settings.RunInBackground;
        StartWithWindowsToggle.IsOn = await _settings.GetStartWithWindowsAsync();
        ThemeCombo.SelectedIndex = _settings.Theme switch
        {
            "light" => 1,
            "dark" => 2,
            _ => 0
        };
        TerminalLaunchModeCombo.SelectedIndex = _settings.TerminalLaunchMode == TerminalLaunchMode.NewWindow ? 1 : 0;
        _loading = false;
        UpdateEnvironments();
        UpdateConnection();
    }

    private void UpdateEnvironments()
    {
        _environments.Clear();
        foreach (var environment in _workspace.Environments)
        {
            _environments.Add(new EnvironmentSettingItem(
                environment.Id,
                environment.DisplayName,
                environment.CodexVersion is null
                    ? environment.StatusMessage ?? environment.Availability.ToString()
                    : $"Codex {environment.CodexVersion} · {environment.Availability}",
                environment.IsAvailable,
                environment.IsAvailable && _settings.IsAgentEnvironmentEnabled(environment.Id)));
        }
    }

    private void UpdateConnection() =>
        GitHubStatusText.Text = _github.IsAuthenticated ? "GitHub connected" : "GitHub not connected";

    private async void OnAgentEnvironmentToggled(object sender, RoutedEventArgs e)
    {
        if (_loading || sender is not ToggleSwitch { Tag: string environmentId } toggle)
        {
            return;
        }

        _settings.SetAgentEnvironmentEnabled(environmentId, toggle.IsOn);
        try
        {
            await _workspace.RefreshAsync(preserveExisting: true);
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void OnTerminalLaunchModeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_loading && TerminalLaunchModeCombo.SelectedItem is ComboBoxItem { Tag: string mode })
        {
            _settings.TerminalLaunchMode = string.Equals(mode, "NewWindow", StringComparison.OrdinalIgnoreCase)
                ? TerminalLaunchMode.NewWindow
                : TerminalLaunchMode.NewTab;
        }
    }

    private void OnManageGitHubClicked(object sender, RoutedEventArgs e) => ManageGitHubRequested?.Invoke(this, EventArgs.Empty);

    private async void OnAboutLinkClicked(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string url } && Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            await Windows.System.Launcher.LaunchUriAsync(uri);
        }
    }

    private void OnThemeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_loading && ThemeCombo.SelectedItem is ComboBoxItem { Tag: string theme })
        {
            _settings.Theme = theme;
            ThemeChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private void OnNotificationsToggled(object sender, RoutedEventArgs e)
    {
        if (!_loading)
        {
            _settings.NotificationsEnabled = NotificationsToggle.IsOn;
        }
    }

    private void OnTestNotificationClicked(object sender, RoutedEventArgs e)
    {
        if (_notifications.ShowTestNotification(out var message))
        {
            ShowMessage(message, InfoBarSeverity.Success);
        }
        else
        {
            ShowMessage(message, InfoBarSeverity.Error);
        }
    }

    private async void OnRunInBackgroundToggled(object sender, RoutedEventArgs e)
    {
        if (_loading)
        {
            return;
        }

        _settings.RunInBackground = RunInBackgroundToggle.IsOn;
        if (RunInBackgroundToggle.IsOn)
        {
            _tray.SetVisible(true);
            _backgroundRefresh.Start();
        }
        else
        {
            await _backgroundRefresh.StopAsync();
            _tray.SetVisible(false);
        }
    }

    private async void OnStartWithWindowsToggled(object sender, RoutedEventArgs e)
    {
        if (_loading)
        {
            return;
        }

        var enabled = await _settings.SetStartWithWindowsAsync(StartWithWindowsToggle.IsOn);
        if (enabled != StartWithWindowsToggle.IsOn)
        {
            _loading = true;
            StartWithWindowsToggle.IsOn = enabled;
            _loading = false;
            ShowMessage("Windows did not allow the startup setting to be changed.", InfoBarSeverity.Warning);
        }
    }

    private void ShowMessage(string message, InfoBarSeverity severity)
    {
        SettingsInfoBar.Message = message;
        SettingsInfoBar.Severity = severity;
        SettingsInfoBar.IsOpen = true;
    }

    private void OnPageSizeChanged(object sender, SizeChangedEventArgs e)
    {
        var narrow = e.NewSize.Width < 980;
        LeftSettingsColumn.Width = new GridLength(1, GridUnitType.Star);
        RightSettingsColumn.Width = narrow ? new GridLength(0) : new GridLength(1, GridUnitType.Star);
        Grid.SetColumn(GeneralSettingsColumn, narrow ? 0 : 1);
        Grid.SetRow(GeneralSettingsColumn, narrow ? 1 : 0);
        Grid.SetColumnSpan(GeneralSettingsColumn, narrow ? 2 : 1);
    }

    private static string GetPackageVersion()
    {
        var version = Windows.ApplicationModel.Package.Current.Id.Version;
        return $"{version.Major}.{version.Minor}.{version.Build}.{version.Revision}";
    }

    private sealed class EnvironmentSettingItem(
        string id,
        string displayName,
        string versionAndStatus,
        bool canEnable,
        bool isEnabled)
    {
        public string Id { get; } = id;
        public string DisplayName { get; } = displayName;
        public string VersionAndStatus { get; } = versionAndStatus;
        public bool CanEnable { get; } = canEnable;
        public bool IsEnabled { get; set; } = isEnabled;
    }
}
