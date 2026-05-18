using Openza.Flow.Core.Services;
using Xunit;

namespace Openza.Flow.Tests;

public sealed class NotificationLaunchUrlValidatorTests
{
    [Fact]
    public void AllowsHttpsGitHubUrl()
    {
        var isValid = NotificationLaunchUrlValidator.TryCreateGitHubUrl(
            "https://github.com/openza/flow/pull/12",
            out var uri);

        Assert.True(isValid);
        Assert.Equal("https://github.com/openza/flow/pull/12", uri.ToString());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not a uri")]
    [InlineData("http://github.com/openza/flow/pull/12")]
    [InlineData("https://github.com.evil.test/openza/flow/pull/12")]
    [InlineData("mailto:security@example.com")]
    [InlineData("https://github.com/")]
    public void RejectsMalformedOrUnexpectedUrls(string? value)
    {
        var isValid = NotificationLaunchUrlValidator.TryCreateGitHubUrl(value, out _);

        Assert.False(isValid);
    }
}
