using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Openza.Flow.Core.Models;
using Openza.Flow.Core.Services;
using Openza.Flow.Services;
using Openza.Flow.ViewModels;
using Windows.ApplicationModel.DataTransfer;

namespace Openza.Flow.Pages;

public sealed partial class AgentSessionsPage : Page
{
    private static readonly TimeSpan SnapshotFreshness = TimeSpan.FromMinutes(5);
    private bool _isNarrow;
    private bool _isCompact;
    private bool _syncingFilters;

    public AgentSessionsPage(IAgentSessionWorkspace workspace, AppSettingsService settings, ITerminalLauncher terminalLauncher)
    {
        ViewModel = new AgentSessionsViewModel(workspace, settings, terminalLauncher);
        InitializeComponent();
        DataContext = ViewModel;
        Loaded += OnPageLoaded;
        Unloaded += OnPageUnloaded;
    }

    public AgentSessionsViewModel ViewModel { get; }

    public Task ActivateAsync()
    {
        ViewModel.SetActive(true);
        ViewModel.RestoreSnapshotPresentation();
        SynchronizeFilterControls();
        UpdateStatePresentation();
        _ = CompleteActivationAsync();
        return Task.CompletedTask;
    }

    public Task DeactivateAsync() => ViewModel.DeactivateAsync();

    private async Task CompleteActivationAsync()
    {
        try
        {
            await ViewModel.ActivateAsync(SnapshotFreshness);
            ViewModel.RestoreSnapshotPresentation();
            SynchronizeFilterControls();
            UpdateStatePresentation();
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void OnPageLoaded(object sender, RoutedEventArgs e)
    {
        ViewModel.SetActive(true);
        ViewModel.RestoreSnapshotPresentation();
        SynchronizeFilterControls();
        UpdateStatePresentation();
    }

    private void OnPageUnloaded(object sender, RoutedEventArgs e)
    {
        ViewModel.SetActive(false);
        _ = ViewModel.DeactivateAsync();
    }

    public async Task SelectSessionAsync(AgentSessionKey key)
    {
        await ViewModel.SelectByKeyAsync(key);
        SessionList.SelectedItem = ViewModel.SelectedSession;
        SessionList.ScrollIntoView(ViewModel.SelectedSession);
        UpdateSelectionPresentation();
    }

    public void FilterToProject(DeveloperProjectSummary project)
    {
        ShowProjectGrouping();
        ViewModel.SelectedEnvironmentId = project.Environment.Id;
        ViewModel.SearchText = project.RootPath;
        SessionSearchBox.Text = project.RootPath;
        SynchronizeFilterControls();
        UpdateStatePresentation();
    }

    public void ShowProjectGrouping()
    {
        ViewModel.GroupingMode = AgentSessionGroupingMode.Project;
        SynchronizeFilterControls();
        UpdateStatePresentation();
    }

    private async void OnRefreshClicked(object sender, RoutedEventArgs e)
    {
        try
        {
            await ViewModel.RefreshAsync(preserveExisting: true);
            SynchronizeFilterControls();
        }
        catch (OperationCanceledException)
        {
        }
        UpdateStatePresentation();
    }

    private void OnLoadMoreSessionsClicked(object sender, RoutedEventArgs e)
    {
        ViewModel.LoadMoreSessions();
        UpdateStatePresentation();
    }

    private void OnSearchTextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (args.Reason != AutoSuggestionBoxTextChangeReason.UserInput)
        {
            return;
        }

        ViewModel.SearchText = sender.Text;
        UpdateStatePresentation();
    }

    private void OnAgentFilterChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncingFilters)
        {
            return;
        }

        ViewModel.SelectedAgentId = (AgentFilterList.SelectedItem as AgentSessionFilterOption)?.Id;
        SynchronizeFilterControls();
        UpdateStatePresentation();
    }

    private void OnEnvironmentFilterChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncingFilters)
        {
            return;
        }

        ViewModel.SelectedEnvironmentId = (EnvironmentFilterList.SelectedItem as AgentEnvironmentFilterOption)?.Id;
        SynchronizeFilterControls();
        UpdateStatePresentation();
    }

    private void OnSourceFilterChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncingFilters)
        {
            return;
        }

        ViewModel.SelectedSource = (SourceFilterList.SelectedItem as AgentSessionFilterOption)?.Id;
        SynchronizeFilterControls();
        UpdateStatePresentation();
    }

    private void OnGroupingFilterChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncingFilters || GroupingFilterList.SelectedItem is not AgentSessionGroupingOption option)
        {
            return;
        }

        ViewModel.GroupingMode = option.Mode;
        SessionList.SelectedItem = ViewModel.SelectedSession;
        SynchronizeFilterControls();
        UpdateStatePresentation();
        UpdateSelectionPresentation();
    }

    private void OnCompactAgentChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncingFilters)
        {
            return;
        }

        ViewModel.SelectedAgentId = (CompactAgentCombo.SelectedItem as AgentSessionFilterOption)?.Id;
        SynchronizeFilterControls();
        UpdateStatePresentation();
    }

    private void OnCompactEnvironmentChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncingFilters)
        {
            return;
        }

        ViewModel.SelectedEnvironmentId = (CompactEnvironmentCombo.SelectedItem as AgentEnvironmentFilterOption)?.Id;
        SynchronizeFilterControls();
        UpdateStatePresentation();
    }

    private void OnCompactSourceChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncingFilters)
        {
            return;
        }

        ViewModel.SelectedSource = (CompactSourceCombo.SelectedItem as AgentSessionFilterOption)?.Id;
        SynchronizeFilterControls();
        UpdateStatePresentation();
    }

    private void OnCompactGroupingChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncingFilters || CompactGroupingCombo.SelectedItem is not AgentSessionGroupingOption option)
        {
            return;
        }

        ViewModel.GroupingMode = option.Mode;
        SynchronizeFilterControls();
        UpdateStatePresentation();
    }

    private async void OnSessionSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var selectionTask = ViewModel.SelectAsync(SessionList.SelectedItem as AgentSessionListItem);
        ActionInfoBar.IsOpen = false;
        UpdateSelectionPresentation();
        SelectedDetail.ChangeView(null, 0, null, disableAnimation: true);
        await selectionTask;
        UpdateSelectionPresentation();
    }

    private async void OnResumeClicked(object sender, RoutedEventArgs e)
    {
        var validation = await ViewModel.ValidateResumeAsync();
        if (!validation.IsValid)
        {
            ShowActionMessage(validation.Message ?? "The session cannot be resumed.", InfoBarSeverity.Error);
            return;
        }

        try
        {
            await ViewModel.ResumeAsync();
            ShowActionMessage("Session opened in Windows Terminal.", InfoBarSeverity.Success);
        }
        catch (TerminalLaunchException exception)
        {
            ShowActionMessage(exception.Message, InfoBarSeverity.Error);
        }
        catch
        {
            ShowActionMessage("Windows Terminal could not be started. Copy the resume command and run it manually.", InfoBarSeverity.Error);
        }
    }

    private void OnCopyCommandClicked(object sender, RoutedEventArgs e)
    {
        if (ViewModel.CopyableResumeCommand is { } command)
        {
            CopyText(command);
            ShowActionMessage("Resume command copied.", InfoBarSeverity.Success);
        }
    }

    private void OnCopySessionIdClicked(object sender, RoutedEventArgs e)
    {
        if (ViewModel.SelectedSession is { } session)
        {
            CopyText(session.SessionId);
            ShowActionMessage("Session ID copied.", InfoBarSeverity.Success);
        }
    }

    private async void OnOpenFolderClicked(object sender, RoutedEventArgs e)
    {
        if (ViewModel.SelectedSession?.Summary is not { Environment.Kind: AgentEnvironmentKind.Windows } session)
        {
            return;
        }

        try
        {
            var folder = await Windows.Storage.StorageFolder.GetFolderFromPathAsync(session.WorkingDirectory);
            if (!await Windows.System.Launcher.LaunchFolderAsync(folder))
            {
                ShowActionMessage("The folder could not be opened.", InfoBarSeverity.Error);
            }
        }
        catch
        {
            ShowActionMessage("The original Windows folder no longer exists.", InfoBarSeverity.Error);
        }
    }

    private void OnBackClicked(object sender, RoutedEventArgs e)
    {
        SessionList.SelectedItem = null;
        UpdateSelectionPresentation();
    }

    private void OnPageSizeChanged(object sender, SizeChangedEventArgs e)
    {
        _isNarrow = e.NewSize.Width < 900;
        _isCompact = e.NewSize.Width < 1200;
        FilterColumn.Width = _isCompact ? new GridLength(0) : new GridLength(220);
        FilterSeparatorColumn.Width = _isCompact ? new GridLength(0) : new GridLength(1);
        FilterRail.Visibility = _isCompact ? Visibility.Collapsed : Visibility.Visible;
        FilterButton.Visibility = _isCompact ? Visibility.Visible : Visibility.Collapsed;
        DetailColumn.Width = _isNarrow
            ? new GridLength(1, GridUnitType.Star)
            : new GridLength(_isCompact ? 390 : 430);
        DetailSeparatorColumn.Width = _isNarrow ? new GridLength(0) : new GridLength(1);
        ListColumn.Width = new GridLength(1, GridUnitType.Star);
        UpdateSelectionPresentation();
    }

    private void UpdateStatePresentation()
    {
        EmptyStatePanel.Visibility = ViewModel.ShowEmptyState ? Visibility.Visible : Visibility.Collapsed;
        SessionList.Visibility = ViewModel.ShowEmptyState ? Visibility.Collapsed : Visibility.Visible;
        LoadMoreSessionsButton.Visibility = !ViewModel.ShowEmptyState && ViewModel.HasMoreSessions
            ? Visibility.Visible
            : Visibility.Collapsed;
        var showProviderWarning = ViewModel.State is AgentSessionsState.PartialFailure or AgentSessionsState.Unavailable;
        var showProgressStatus = ViewModel.State is AgentSessionsState.Initial or AgentSessionsState.Loading;
        NormalStatusPanel.Visibility = showProgressStatus ? Visibility.Visible : Visibility.Collapsed;
        StatusInfoBar.Visibility = showProviderWarning ? Visibility.Visible : Visibility.Collapsed;
        StatusInfoBar.IsOpen = showProviderWarning;
        StatusInfoBar.Severity = ViewModel.State switch
        {
            AgentSessionsState.PartialFailure => InfoBarSeverity.Warning,
            AgentSessionsState.Unavailable => InfoBarSeverity.Error,
            _ => InfoBarSeverity.Informational
        };
    }

    private void UpdateSelectionPresentation()
    {
        var hasSelection = ViewModel.SelectedSession is not null;
        NoSelectionPanel.Visibility = hasSelection ? Visibility.Collapsed : Visibility.Visible;
        SelectedDetail.Visibility = hasSelection ? Visibility.Visible : Visibility.Collapsed;
        OpenFolderButton.Visibility = ViewModel.SelectedSession?.Summary.Environment.Kind == AgentEnvironmentKind.Windows
            ? Visibility.Visible
            : Visibility.Collapsed;
        var previewUnavailable = ViewModel.Preview is { IsAvailable: false };
        PreviewUnavailableInfoBar.IsOpen = previewUnavailable;
        PreviewUnavailableInfoBar.Message = ViewModel.Preview?.UnavailableReason ?? string.Empty;
        PreviewMessages.Visibility = previewUnavailable ? Visibility.Collapsed : Visibility.Visible;

        if (_isNarrow)
        {
            ListPanel.Visibility = hasSelection ? Visibility.Collapsed : Visibility.Visible;
            DetailPanel.Visibility = hasSelection ? Visibility.Visible : Visibility.Collapsed;
            BackButton.Visibility = hasSelection ? Visibility.Visible : Visibility.Collapsed;
        }
        else
        {
            ListPanel.Visibility = Visibility.Visible;
            DetailPanel.Visibility = Visibility.Visible;
            BackButton.Visibility = Visibility.Collapsed;
        }
    }

    private void SynchronizeFilterControls()
    {
        _syncingFilters = true;
        try
        {
            AgentFilterList.SelectedItem = FindOption(AgentFilterList.ItemsSource, ViewModel.SelectedAgentId);
            EnvironmentFilterList.SelectedItem = FindOption(EnvironmentFilterList.ItemsSource, ViewModel.SelectedEnvironmentId);
            SourceFilterList.SelectedItem = FindOption(SourceFilterList.ItemsSource, ViewModel.SelectedSource);
            GroupingFilterList.SelectedItem = ViewModel.GroupingOptions.First(option => option.Mode == ViewModel.GroupingMode);

            CompactAgentCombo.SelectedItem = FindOption(CompactAgentCombo.ItemsSource, ViewModel.SelectedAgentId);
            CompactEnvironmentCombo.SelectedItem = FindOption(CompactEnvironmentCombo.ItemsSource, ViewModel.SelectedEnvironmentId);
            CompactSourceCombo.SelectedItem = FindOption(CompactSourceCombo.ItemsSource, ViewModel.SelectedSource);
            CompactGroupingCombo.SelectedItem = ViewModel.GroupingOptions.First(option => option.Mode == ViewModel.GroupingMode);
        }
        finally
        {
            _syncingFilters = false;
        }
    }

    private static object? FindOption(object? itemsSource, string? id)
    {
        return itemsSource switch
        {
            IEnumerable<AgentSessionFilterOption> options => options.FirstOrDefault(
                option => string.Equals(option.Id, id, StringComparison.OrdinalIgnoreCase)),
            IEnumerable<AgentEnvironmentFilterOption> options => options.FirstOrDefault(
                option => string.Equals(option.Id, id, StringComparison.OrdinalIgnoreCase)),
            _ => null
        };
    }

    private void ShowActionMessage(string message, InfoBarSeverity severity)
    {
        ActionInfoBar.Message = message;
        ActionInfoBar.Severity = severity;
        ActionInfoBar.IsOpen = true;
    }

    private static void CopyText(string text)
    {
        var package = new DataPackage();
        package.SetText(text);
        Clipboard.SetContent(package);
    }
}

public sealed class AgentSessionMessageTemplateSelector : DataTemplateSelector
{
    public DataTemplate? UserTemplate { get; set; }

    public DataTemplate? AssistantTemplate { get; set; }

    protected override DataTemplate? SelectTemplateCore(object item)
    {
        return item is AgentSessionMessage { Role: AgentSessionMessageRole.User }
            ? UserTemplate
            : AssistantTemplate;
    }

    protected override DataTemplate? SelectTemplateCore(object item, DependencyObject container) =>
        SelectTemplateCore(item);
}
