using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Openza.Flow.Core.Models;

namespace Openza.Flow.ViewModels;

public sealed class RepositoryActivityListItem
{
    public RepositoryActivityListItem(GithubRelease release)
    {
        Title = string.IsNullOrWhiteSpace(release.Name) ? release.TagName : release.Name;
        HtmlUrl = release.HtmlUrl;
        Repository = release.Repository.FullName;
        PrimaryDetail = release.TagName;
        SecondaryDetail = string.IsNullOrWhiteSpace(release.Author) ? "Release" : $"by {release.Author}";
        TimestampText = FormatRelative(release.SortTimestamp);
        Badge = release.Draft ? "Draft" : release.Prerelease ? "Prerelease" : "Release";
        SecondaryBadge = release.Draft && release.Prerelease ? "Prerelease" : string.Empty;
        ApplyStatusBrushes();
    }

    public RepositoryActivityListItem(GithubWorkflowRun run)
    {
        Title = string.IsNullOrWhiteSpace(run.DisplayTitle) ? run.WorkflowName : run.DisplayTitle;
        HtmlUrl = run.HtmlUrl;
        Repository = run.Repository.FullName;
        PrimaryDetail = string.IsNullOrWhiteSpace(run.WorkflowName) ? $"Run #{run.RunNumber}" : $"{run.WorkflowName} #{run.RunNumber}";
        SecondaryDetail = $"{run.Branch} / {run.Event}";
        TimestampText = FormatRelative(run.CreatedAt);
        Badge = string.IsNullOrWhiteSpace(run.Conclusion) ? run.Status : run.Conclusion;
        SecondaryBadge = run.Status;
        ApplyStatusBrushes();
    }

    public string Title { get; }

    public string HtmlUrl { get; }

    public string Repository { get; }

    public string PrimaryDetail { get; }

    public string SecondaryDetail { get; }

    public string TimestampText { get; }

    public string Badge { get; }

    public string SecondaryBadge { get; }

    public Visibility SecondaryBadgeVisibility => string.IsNullOrWhiteSpace(SecondaryBadge)
        || string.Equals(Badge, SecondaryBadge, StringComparison.OrdinalIgnoreCase)
            ? Visibility.Collapsed
            : Visibility.Visible;

    public Brush BadgeBackground { get; private set; } = Brush("#f6f8fa");

    public Brush BadgeBorder { get; private set; } = Brush("#d0d7de");

    public Brush BadgeForeground { get; private set; } = Brush("#57606a");

    public Brush SecondaryBadgeBackground { get; private set; } = Brush("#f6f8fa");

    public Brush SecondaryBadgeBorder { get; private set; } = Brush("#d0d7de");

    public Brush SecondaryBadgeForeground { get; private set; } = Brush("#57606a");

    private void ApplyStatusBrushes()
    {
        (BadgeBackground, BadgeBorder, BadgeForeground) = Palette(Badge);
        (SecondaryBadgeBackground, SecondaryBadgeBorder, SecondaryBadgeForeground) = Palette(SecondaryBadge);
    }

    private static (Brush Background, Brush Border, Brush Foreground) Palette(string status)
    {
        return status.ToLowerInvariant() switch
        {
            "success" => (Brush("#f0fff4"), Brush("#8ddb9b"), Brush("#1a7f37")),
            "completed" => (Brush("#f0fff4"), Brush("#8ddb9b"), Brush("#1a7f37")),
            "failure" => (Brush("#fff5f5"), Brush("#ffb4ad"), Brush("#cf222e")),
            "cancelled" => (Brush("#fff5f5"), Brush("#ffb4ad"), Brush("#cf222e")),
            "timed_out" => (Brush("#fff5f5"), Brush("#ffb4ad"), Brush("#cf222e")),
            "action_required" => (Brush("#fff8c5"), Brush("#eac54f"), Brush("#9a6700")),
            "in_progress" => (Brush("#ddf4ff"), Brush("#80ccff"), Brush("#0969da")),
            "queued" => (Brush("#f6f8fa"), Brush("#d0d7de"), Brush("#57606a")),
            "waiting" => (Brush("#f6f8fa"), Brush("#d0d7de"), Brush("#57606a")),
            "requested" => (Brush("#f6f8fa"), Brush("#d0d7de"), Brush("#57606a")),
            "draft" => (Brush("#fff8c5"), Brush("#eac54f"), Brush("#9a6700")),
            "prerelease" => (Brush("#faf5ff"), Brush("#d8b9ff"), Brush("#8250df")),
            "release" => (Brush("#f0fff4"), Brush("#8ddb9b"), Brush("#1a7f37")),
            _ => (Brush("#f6f8fa"), Brush("#d0d7de"), Brush("#57606a"))
        };
    }

    private static SolidColorBrush Brush(string hex)
    {
        var value = Convert.ToUInt32(hex.TrimStart('#'), 16);
        return new SolidColorBrush(ColorHelper.FromArgb(
            255,
            (byte)((value >> 16) & 0xff),
            (byte)((value >> 8) & 0xff),
            (byte)(value & 0xff)));
    }

    private static string FormatRelative(DateTimeOffset timestamp)
    {
        var delta = DateTimeOffset.Now - timestamp;
        if (delta.TotalMinutes < 1)
        {
            return "just now";
        }

        if (delta.TotalHours < 1)
        {
            return $"{Math.Max(1, (int)delta.TotalMinutes)}m ago";
        }

        if (delta.TotalDays < 1)
        {
            return $"{Math.Max(1, (int)delta.TotalHours)}h ago";
        }

        if (delta.TotalDays < 7)
        {
            return $"{Math.Max(1, (int)delta.TotalDays)}d ago";
        }

        return timestamp.ToString("MMM d");
    }
}
