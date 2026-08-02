using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Openza.Flow.ViewModels;

namespace Openza.Flow.Controls;

public sealed partial class PullRequestRowControl : UserControl
{
    public static readonly DependencyProperty IsCompactProperty =
        DependencyProperty.Register(
            nameof(IsCompact),
            typeof(bool),
            typeof(PullRequestRowControl),
            new PropertyMetadata(false, OnLayoutPropertyChanged));

    public static readonly DependencyProperty ShowSummaryProperty =
        DependencyProperty.Register(
            nameof(ShowSummary),
            typeof(bool),
            typeof(PullRequestRowControl),
            new PropertyMetadata(true, OnLayoutPropertyChanged));

    public static readonly DependencyProperty ShowAuthorProperty =
        DependencyProperty.Register(
            nameof(ShowAuthor),
            typeof(bool),
            typeof(PullRequestRowControl),
            new PropertyMetadata(true, OnLayoutPropertyChanged));

    public PullRequestRowControl()
    {
        InitializeComponent();
        UpdateLayoutProperties();
    }

    public event EventHandler<PrListItem>? CopyNumberRequested;

    public event EventHandler<PrListItem>? OpenRequested;

    public bool IsCompact
    {
        get => (bool)GetValue(IsCompactProperty);
        set => SetValue(IsCompactProperty, value);
    }

    public bool ShowSummary
    {
        get => (bool)GetValue(ShowSummaryProperty);
        set => SetValue(ShowSummaryProperty, value);
    }

    public bool ShowAuthor
    {
        get => (bool)GetValue(ShowAuthorProperty);
        set => SetValue(ShowAuthorProperty, value);
    }

    public Thickness RowPadding { get; private set; }

    public Thickness NumberButtonMargin { get; private set; }

    public Thickness NumberButtonPadding { get; private set; }

    public Thickness BadgePadding { get; private set; }

    public double NumberButtonMinWidth { get; private set; }

    public double NumberButtonHeight { get; private set; }

    public double NumberTextFontSize { get; private set; }

    public double TitleFontSize { get; private set; }

    public int TitleMaxLines { get; private set; }

    public double ContentSpacing { get; private set; }

    public Visibility SummaryVisibility => ShowSummary ? Visibility.Visible : Visibility.Collapsed;

    public Visibility AuthorVisibility => ShowAuthor ? Visibility.Visible : Visibility.Collapsed;

    public Visibility NormalVisibility => IsCompact ? Visibility.Collapsed : Visibility.Visible;

    public Visibility CompactVisibility => IsCompact ? Visibility.Visible : Visibility.Collapsed;

    private static void OnLayoutPropertyChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs args)
    {
        ((PullRequestRowControl)dependencyObject).UpdateLayoutProperties();
    }

    private void UpdateLayoutProperties()
    {
        RowPadding = IsCompact ? new Thickness(10) : new Thickness(10, 12, 10, 12);
        NumberButtonMargin = IsCompact ? new Thickness(0, 2, 10, 0) : new Thickness(0, 3, 16, 0);
        NumberButtonPadding = IsCompact ? new Thickness(7, 0, 7, 0) : new Thickness(9, 0, 9, 0);
        BadgePadding = IsCompact ? new Thickness(7, 2, 7, 2) : new Thickness(8, 3, 8, 3);
        NumberButtonMinWidth = IsCompact ? 0 : 56;
        NumberButtonHeight = IsCompact ? 23 : 28;
        NumberTextFontSize = IsCompact ? 12 : 14;
        TitleFontSize = IsCompact ? 13 : 14;
        TitleMaxLines = IsCompact ? 1 : 2;
        ContentSpacing = IsCompact ? 5 : 4;

        Bindings.Update();
    }

    private PrListItem? Item => DataContext as PrListItem;

    private void OnCopyNumberClicked(object sender, RoutedEventArgs e)
    {
        if (Item is { } item)
        {
            CopyNumberRequested?.Invoke(this, item);
        }
    }

    private void OnOpenClicked(object sender, RoutedEventArgs e)
    {
        if (Item is { } item)
        {
            OpenRequested?.Invoke(this, item);
        }
    }

    private void OnRowDoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if (Item is { } item)
        {
            e.Handled = true;
            OpenRequested?.Invoke(this, item);
        }
    }
}
