using System.Text;
using Openza.Flow.Core.Models;

namespace Openza.Flow.Core.Services;

public static class AgentSessionUtilities
{
    public static string NormalizeDisplayText(string value) => value
        .Replace("â€¦", "…", StringComparison.Ordinal)
        .Replace("â€“", "–", StringComparison.Ordinal)
        .Replace("â€”", "—", StringComparison.Ordinal)
        .Replace("â€™", "’", StringComparison.Ordinal)
        .Replace("â€˜", "‘", StringComparison.Ordinal)
        .Replace("â€œ", "“", StringComparison.Ordinal)
        .Replace("â€", "”", StringComparison.Ordinal)
        .Replace("â€¢", "•", StringComparison.Ordinal)
        .Replace("â†’", "→", StringComparison.Ordinal)
        .Replace("â†", "←", StringComparison.Ordinal)
        .Replace("Â ", " ", StringComparison.Ordinal);

    public static IReadOnlyList<AgentSessionSummary> MergeAndSort(IEnumerable<AgentSessionSummary> sessions) =>
        sessions
            .GroupBy(session => session.Key)
            .Select(group => group.OrderByDescending(session => session.RecencyAt).First())
            .OrderByDescending(session => session.RecencyAt)
            .ToList();

    public static bool Matches(AgentSessionSummary session, string? query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return true;
        }

        var comparison = StringComparison.OrdinalIgnoreCase;
        return session.Title.Contains(query, comparison)
            || session.WorkingDirectory.Contains(query, comparison)
            || session.Environment.DisplayName.Contains(query, comparison)
            || session.Source.Contains(query, comparison)
            || (session.Git?.RepositoryName?.Contains(query, comparison) ?? false);
    }

    public static AgentSessionDateGroup GetDateGroup(DateTimeOffset recency, DateTimeOffset now)
    {
        var localDate = recency.ToLocalTime().Date;
        var today = now.ToLocalTime().Date;
        if (localDate == today)
        {
            return AgentSessionDateGroup.Today;
        }

        if (localDate == today.AddDays(-1))
        {
            return AgentSessionDateGroup.Yesterday;
        }

        var startOfWeek = today.AddDays(-(((int)today.DayOfWeek + 6) % 7));
        return localDate >= startOfWeek ? AgentSessionDateGroup.ThisWeek : AgentSessionDateGroup.Older;
    }

    public static IReadOnlyList<string> DecodeWslDistributionList(byte[] output)
    {
        if (output.Length == 0)
        {
            return [];
        }

        Encoding encoding;
        var offset = 0;
        if (output.Length >= 2 && output[0] == 0xff && output[1] == 0xfe)
        {
            encoding = Encoding.Unicode;
            offset = 2;
        }
        else if (output.Length >= 2 && output[0] == 0xfe && output[1] == 0xff)
        {
            encoding = Encoding.BigEndianUnicode;
            offset = 2;
        }
        else if (output.Skip(1).Where((_, index) => index % 2 == 0).Any(value => value == 0))
        {
            encoding = Encoding.Unicode;
        }
        else
        {
            encoding = Encoding.UTF8;
        }

        return encoding.GetString(output, offset, output.Length - offset)
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(value => value.Trim('\0'))
            .Where(value => value.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
