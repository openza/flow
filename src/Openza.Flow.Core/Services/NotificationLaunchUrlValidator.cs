namespace Openza.Flow.Core.Services;

public static class NotificationLaunchUrlValidator
{
    public static bool TryCreateGitHubUrl(string? value, out Uri uri)
    {
        uri = null!;
        if (string.IsNullOrWhiteSpace(value)
            || !Uri.TryCreate(value, UriKind.Absolute, out var candidate)
            || !string.Equals(candidate.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(candidate.Host, "github.com", StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(candidate.AbsolutePath)
            || candidate.AbsolutePath == "/")
        {
            return false;
        }

        uri = candidate;
        return true;
    }
}
