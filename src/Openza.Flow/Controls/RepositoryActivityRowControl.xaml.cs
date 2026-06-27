using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Openza.Flow.ViewModels;

namespace Openza.Flow.Controls;

public sealed partial class RepositoryActivityRowControl : UserControl
{
    public RepositoryActivityRowControl()
    {
        InitializeComponent();
    }

    public event EventHandler<RepositoryActivityListItem>? OpenRequested;

    private RepositoryActivityListItem? Item => DataContext as RepositoryActivityListItem;

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
