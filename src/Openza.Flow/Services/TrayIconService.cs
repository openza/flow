using System.Drawing;
using System.Windows.Forms;

namespace Openza.Flow.Services;

public sealed class TrayIconService : IDisposable
{
    private readonly NotifyIcon _notifyIcon;
    private bool _disposed;

    public TrayIconService(string iconPath)
    {
        var resolvedIconPath = Path.Combine(AppContext.BaseDirectory, iconPath);
        _notifyIcon = new NotifyIcon
        {
            Icon = File.Exists(resolvedIconPath) ? new Icon(resolvedIconPath) : SystemIcons.Application,
            Text = "Openza Flow",
            Visible = true,
            ContextMenuStrip = BuildMenu()
        };
        _notifyIcon.DoubleClick += (_, _) => OpenRequested?.Invoke(this, EventArgs.Empty);
    }

    public event EventHandler? OpenRequested;

    public event EventHandler? RefreshRequested;

    public event EventHandler? ExitRequested;

    public void ShowBackgroundHint()
    {
        _notifyIcon.ShowBalloonTip(3000, "Openza Flow", "Flow is still watching pull requests in the background.", ToolTipIcon.Info);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
    }

    private ContextMenuStrip BuildMenu()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("Open Flow", null, (_, _) => OpenRequested?.Invoke(this, EventArgs.Empty));
        menu.Items.Add("Refresh now", null, (_, _) => RefreshRequested?.Invoke(this, EventArgs.Empty));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => ExitRequested?.Invoke(this, EventArgs.Empty));
        return menu;
    }
}
