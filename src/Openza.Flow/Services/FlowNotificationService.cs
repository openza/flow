using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;
using Openza.Flow.Core.Models;

namespace Openza.Flow.Services;

public sealed class FlowNotificationService : IDisposable
{
    private bool _registered;

    public event EventHandler<string?>? NotificationActivated;

    public void Initialize()
    {
        if (_registered)
        {
            return;
        }

        try
        {
            AppNotificationManager.Default.NotificationInvoked += OnNotificationInvoked;
            AppNotificationManager.Default.Register();
            _registered = true;
        }
        catch (Exception exception)
        {
            AppLog.Write(exception);
        }
    }

    public void ShowNewReviewRequests(IReadOnlyList<PullRequest> pullRequests)
    {
        if (!_registered || pullRequests.Count == 0)
        {
            return;
        }

        var title = pullRequests.Count == 1 ? "New PR review request" : $"{pullRequests.Count} new PR review requests";
        var body = pullRequests.Count == 1
            ? $"{pullRequests[0].Repository.FullName} #{pullRequests[0].Number}: {pullRequests[0].Title}"
            : "Open Flow to review the latest pull requests.";

        try
        {
            var notification = new AppNotificationBuilder()
                .AddArgument("action", "open")
                .AddArgument("url", pullRequests[0].HtmlUrl)
                .AddText(title)
                .AddText(body)
                .BuildNotification();

            AppNotificationManager.Default.Show(notification);
        }
        catch (Exception exception)
        {
            AppLog.Write(exception);
        }
    }

    public void Dispose()
    {
        if (!_registered)
        {
            return;
        }

        try
        {
            AppNotificationManager.Default.NotificationInvoked -= OnNotificationInvoked;
            AppNotificationManager.Default.Unregister();
        }
        catch
        {
        }
    }

    private void OnNotificationInvoked(AppNotificationManager sender, AppNotificationActivatedEventArgs args)
    {
        args.Arguments.TryGetValue("url", out var url);
        NotificationActivated?.Invoke(this, url);
    }
}
