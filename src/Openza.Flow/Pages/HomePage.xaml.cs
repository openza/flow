using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Openza.Flow.Core.Models;
using Openza.Flow.Core.Services;
using Openza.Flow.Services;
using Openza.Flow.ViewModels;

namespace Openza.Flow.Pages;

public sealed partial class HomePage : Page
{
    private readonly IAgentSessionWorkspace _workspace;
    private readonly ITerminalLauncher _terminalLauncher;
    private readonly AppSettingsService _settings;

    public HomePage(
        IAgentSessionWorkspace workspace,
        GitHubWorkspaceState github,
        ITerminalLauncher terminalLauncher,
        AppSettingsService settings)
    {
        _workspace = workspace;
        _terminalLauncher = terminalLauncher;
        _settings = settings;
        ViewModel = new HomeViewModel(workspace, github);
        InitializeComponent();
        DataContext = ViewModel;
        ViewModel.PresentationChanged += (_, _) => UpdatePresentation();
        Loaded += OnPageLoaded;
        Unloaded += OnPageUnloaded;
    }

    public event EventHandler? ViewSessionsRequested;

    public event EventHandler? ViewActivityRequested;

    public event EventHandler? RefreshGitHubRequested;

    public event EventHandler<AgentSessionKey>? SessionRequested;

    public event EventHandler<DeveloperProjectSummary>? ProjectRequested;

    public HomeViewModel ViewModel { get; }

    public async Task ActivateAsync()
    {
        ViewModel.SetActive(true);
        await ViewModel.ActivateAsync();
        UpdatePresentation();
    }

    private void OnPageLoaded(object sender, RoutedEventArgs e)
    {
        ViewModel.SetActive(true);
        UpdatePresentation();
    }

    private void OnPageUnloaded(object sender, RoutedEventArgs e) => ViewModel.SetActive(false);

    private async void OnRefreshClicked(object sender, RoutedEventArgs e)
    {
        try
        {
            var sessionRefresh = ViewModel.RefreshSessionsAsync();
            RefreshGitHubRequested?.Invoke(this, EventArgs.Empty);
            await sessionRefresh;
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            AppLog.Write(exception);
            ShowActionMessage("The dashboard could not be refreshed. Try again.");
        }

        UpdatePresentation();
    }

    private void OnSearchTextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (args.Reason == AutoSuggestionBoxTextChangeReason.UserInput)
        {
            sender.ItemsSource = ViewModel.Search(sender.Text);
        }
    }

    private void OnSearchSuggestionChosen(AutoSuggestBox sender, AutoSuggestBoxSuggestionChosenEventArgs args)
    {
        if (args.SelectedItem is HomeSearchResult result)
        {
            sender.Text = result.Title;
        }
    }

    private void OnSearchQuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
    {
        var result = args.ChosenSuggestion as HomeSearchResult ?? ViewModel.Search(args.QueryText).FirstOrDefault();
        if (result?.SessionKey is { } sessionKey)
        {
            SessionRequested?.Invoke(this, sessionKey);
        }
        else if (result?.Project is { } project)
        {
            ProjectRequested?.Invoke(this, project);
        }
    }

    private void OnSearchAcceleratorInvoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        GlobalSearch.Focus(FocusState.Keyboard);
        args.Handled = true;
    }

    private async void OnResumeSessionClicked(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: AgentSessionSummary session })
        {
            return;
        }

        try
        {
            var validation = await _terminalLauncher.ValidateAsync(session);
            if (!validation.IsValid)
            {
                ShowActionMessage(validation.Message ?? "The session cannot be resumed.");
                return;
            }

            await _terminalLauncher.LaunchAsync(session, _settings.TerminalLaunchMode);
        }
        catch (TerminalLaunchException exception)
        {
            ShowActionMessage(exception.Message);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            AppLog.Write(exception);
            ShowActionMessage("The session could not be resumed. Try again.");
        }
    }

    private async void OnOpenAttentionClicked(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string url } && Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            await Windows.System.Launcher.LaunchUriAsync(uri);
        }
    }

    private void OnViewSessionsClicked(object sender, RoutedEventArgs e) => ViewSessionsRequested?.Invoke(this, EventArgs.Empty);

    private void OnViewActivityClicked(object sender, RoutedEventArgs e) => ViewActivityRequested?.Invoke(this, EventArgs.Empty);

    private void OnPageSizeChanged(object sender, SizeChangedEventArgs e) =>
        ViewModel.SetViewportHeight(e.NewSize.Height);

    private void UpdatePresentation()
    {
        SessionSkeleton.Visibility = ViewModel.IsRefreshing && !ViewModel.HasSessions ? Visibility.Visible : Visibility.Collapsed;
        NoSessionsState.Visibility = !ViewModel.IsRefreshing && !ViewModel.HasSessions ? Visibility.Visible : Visibility.Collapsed;
        NoAttentionState.Visibility = ViewModel.HasAttention ? Visibility.Collapsed : Visibility.Visible;
        ConnectGitHubButton.Visibility = ViewModel.IsGitHubConnected ? Visibility.Collapsed : Visibility.Visible;
        NoAttentionTitle.Text = ViewModel.IsGitHubConnected ? "Nothing needs attention" : "Connect GitHub for review activity";
        ProviderInfoBar.IsOpen = ViewModel.HasPartialFailure;
        ProviderInfoBar.Severity = ViewModel.HasSessions ? InfoBarSeverity.Warning : InfoBarSeverity.Error;
    }

    private void ShowActionMessage(string message)
    {
        ActionInfoBar.Message = message;
        ActionInfoBar.Severity = InfoBarSeverity.Error;
        ActionInfoBar.IsOpen = true;
    }
}
