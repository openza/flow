using Microsoft.Windows.AppLifecycle;
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
            AppLog.Write($"App notifications registered. Setting: {AppNotificationManager.Default.Setting}.");
        }
        catch (Exception exception)
        {
            AppLog.Write(exception);
        }
    }

    public bool ShowTestNotification(out string message)
    {
        var sent = ShowNotification(
            "Openza Flow notifications are working",
            "Windows can show notifications from Flow.",
            null,
            out message);
        if (sent)
        {
            message = "Sent a test notification.";
        }

        return sent;
    }

    public string? GetLaunchNotificationUrl()
    {
        try
        {
            var args = AppInstance.GetCurrent().GetActivatedEventArgs();
            if (args.Kind == ExtendedActivationKind.AppNotification
                && args.Data is AppNotificationActivatedEventArgs notificationArgs
                && notificationArgs.Arguments.TryGetValue("url", out var url))
            {
                return url;
            }
        }
        catch (Exception exception)
        {
            AppLog.Write(exception);
        }

        return null;
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

        _ = ShowNotification(title, body, pullRequests[0].HtmlUrl, out _);
    }

    public bool CanShowNotifications(out string message)
    {
        if (!_registered)
        {
            message = "Windows notifications are not registered yet. Restart Flow and try again.";
            return false;
        }

        try
        {
            var setting = AppNotificationManager.Default.Setting;
            if (setting == AppNotificationSetting.Enabled)
            {
                message = "Windows notifications are enabled.";
                return true;
            }

            message = setting switch
            {
                AppNotificationSetting.DisabledForApplication => "Windows notifications are turned off for Openza Flow.",
                AppNotificationSetting.DisabledForUser => "Windows notifications are turned off in system settings.",
                AppNotificationSetting.DisabledByGroupPolicy => "Windows notifications are blocked by policy.",
                AppNotificationSetting.DisabledByManifest => "Windows notifications are not enabled in the app package manifest.",
                _ => $"Windows notifications are unavailable: {setting}."
            };
            return false;
        }
        catch (Exception exception)
        {
            AppLog.Write(exception);
            message = "Windows notification status could not be checked. See startup.log for details.";
            return false;
        }
    }

    private bool ShowNotification(string title, string body, string? url, out string message)
    {
        if (!CanShowNotifications(out message))
        {
            AppLog.Write(message);
            return false;
        }

        try
        {
            var builder = new AppNotificationBuilder()
                .AddArgument("action", "open")
                .AddText(title)
                .AddText(body);

            if (!string.IsNullOrWhiteSpace(url))
            {
                builder.AddArgument("url", url);
            }

            AppNotificationManager.Default.Show(builder.BuildNotification());
            message = "Notification sent.";
            return true;
        }
        catch (Exception exception)
        {
            AppLog.Write(exception);
            message = "Windows could not show the notification. See startup.log for details.";
            return false;
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
