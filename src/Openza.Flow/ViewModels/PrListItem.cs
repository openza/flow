using Openza.Flow.Core.Models;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;

namespace Openza.Flow.ViewModels;

public sealed class PrListItem
{
    public PrListItem(PullRequest pullRequest)
    {
        Id = pullRequest.Id;
        Number = pullRequest.Number;
        Title = pullRequest.Title;
        HtmlUrl = pullRequest.HtmlUrl;
        Repository = pullRequest.Repository.FullName;
        Author = pullRequest.Author.Login;
        AuthorAvatarSource = CreateAvatarSource(pullRequest.Author.AvatarUrl);
        UpdatedText = $"Updated {FormatRelative(pullRequest.UpdatedAt)}";
        Detail = $"{pullRequest.HeadRefName} -> {pullRequest.BaseRefName}";
        Badge = pullRequest.Draft ? "Draft" : "Open";
        SecondaryBadge = string.Empty;
        Summary = $"{Repository} #{Number}";
        Labels = pullRequest.Labels.Take(3).Select(label => new LabelChip(label)).ToList();
        ApplyStatusBrushes();
    }

    public PrListItem(ReviewedPullRequest pullRequest)
    {
        Id = pullRequest.Id;
        Number = pullRequest.Number;
        Title = pullRequest.Title;
        HtmlUrl = pullRequest.HtmlUrl;
        Repository = pullRequest.Repository.FullName;
        Author = pullRequest.Author.Login;
        AuthorAvatarSource = CreateAvatarSource(pullRequest.Author.AvatarUrl);
        UpdatedText = $"Reviewed {FormatRelative(pullRequest.ReviewedAt)}";
        Detail = $"{pullRequest.HeadRefName} -> {pullRequest.BaseRefName}";
        Badge = pullRequest.ReviewState switch
        {
            ReviewState.Approved => "Approved",
            ReviewState.ChangesRequested => "Changes requested",
            ReviewState.Commented => "Commented",
            _ => "Reviewed"
        };
        SecondaryBadge = pullRequest.MergeState switch
        {
            MergeState.Merged => "Merged",
            MergeState.Closed => "Closed",
            _ => "Open"
        };
        Summary = $"{Repository} #{Number}";
        ApplyStatusBrushes();
    }

    public PrListItem(CreatedPullRequest pullRequest)
    {
        Id = pullRequest.Id;
        Number = pullRequest.Number;
        Title = pullRequest.Title;
        HtmlUrl = pullRequest.HtmlUrl;
        Repository = pullRequest.Repository.FullName;
        Author = "You";
        AuthorAvatarSource = null;
        UpdatedText = $"Created {FormatRelative(pullRequest.CreatedAt)}";
        Detail = $"{pullRequest.HeadRefName} -> {pullRequest.BaseRefName}";
        Badge = pullRequest.MergeState switch
        {
            MergeState.Merged => "Merged",
            MergeState.Closed => "Closed",
            _ => "Open"
        };
        SecondaryBadge = string.Empty;
        Summary = $"{Repository} #{Number}";
        ApplyStatusBrushes();
    }

    public int Id { get; }

    public int Number { get; }

    public string Title { get; }

    public string HtmlUrl { get; }

    public string Repository { get; }

    public string Author { get; }

    public ImageSource? AuthorAvatarSource { get; }

    public string UpdatedText { get; }

    public string Detail { get; }

    public string Badge { get; }

    public string SecondaryBadge { get; }

    public Visibility SecondaryBadgeVisibility => string.IsNullOrWhiteSpace(SecondaryBadge) ? Visibility.Collapsed : Visibility.Visible;

    public string Summary { get; }

    public string DisplayNumber => $"#{Number}";

    public IReadOnlyList<LabelChip> Labels { get; } = [];

    public Visibility LabelsVisibility => Labels.Count == 0 ? Visibility.Collapsed : Visibility.Visible;

    public string LabelsText => Labels.Count == 0 ? string.Empty : string.Join("  ", Labels.Select(label => label.Name));

    public Brush BadgeBackground { get; private set; } = Brush("#eef2f6");

    public Brush BadgeBorder { get; private set; } = Brush("#cbd5e1");

    private static ImageSource? CreateAvatarSource(string? value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) ? new BitmapImage(uri) : null;

    public Brush BadgeForeground { get; private set; } = Brush("#334155");

    public Brush SecondaryBadgeBackground { get; private set; } = Brush("#eef2f6");

    public Brush SecondaryBadgeBorder { get; private set; } = Brush("#cbd5e1");

    public Brush SecondaryBadgeForeground { get; private set; } = Brush("#334155");

    private void ApplyStatusBrushes()
    {
        (BadgeBackground, BadgeBorder, BadgeForeground) = Palette(Badge);
        (SecondaryBadgeBackground, SecondaryBadgeBorder, SecondaryBadgeForeground) = Palette(SecondaryBadge);
    }

    private static (Brush Background, Brush Border, Brush Foreground) Palette(string status)
    {
        return status.ToLowerInvariant() switch
        {
            "approved" => (Brush("#f0fff4"), Brush("#8ddb9b"), Brush("#1a7f37")),
            "merged" => (Brush("#faf5ff"), Brush("#d8b9ff"), Brush("#8250df")),
            "changes requested" => (Brush("#fff5f5"), Brush("#ffb4ad"), Brush("#cf222e")),
            "closed" => (Brush("#fff5f5"), Brush("#ffb4ad"), Brush("#cf222e")),
            "draft" => (Brush("#f6f8fa"), Brush("#d0d7de"), Brush("#57606a")),
            "commented" => (Brush("#f6f8fa"), Brush("#d0d7de"), Brush("#57606a")),
            "reviewed" => (Brush("#f6f8fa"), Brush("#d0d7de"), Brush("#57606a")),
            "open" => (Brush("#f0fff4"), Brush("#8ddb9b"), Brush("#1a7f37")),
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

public sealed class LabelChip
{
    public LabelChip(GithubLabel label)
    {
        Name = label.Name;
        Background = Brush($"33{label.Color}");
        Border = Brush($"99{label.Color}");
        Foreground = Brush(label.Color);
    }

    public string Name { get; }

    public Brush Background { get; }

    public Brush Border { get; }

    public Brush Foreground { get; }

    private static SolidColorBrush Brush(string hex)
    {
        var normalized = hex.TrimStart('#');
        var argb = normalized.Length == 8
            ? Convert.ToUInt32(normalized, 16)
            : Convert.ToUInt32($"ff{normalized}", 16);

        return new SolidColorBrush(ColorHelper.FromArgb(
            (byte)((argb >> 24) & 0xff),
            (byte)((argb >> 16) & 0xff),
            (byte)((argb >> 8) & 0xff),
            (byte)(argb & 0xff)));
    }
}
