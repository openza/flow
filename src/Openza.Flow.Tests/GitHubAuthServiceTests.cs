using System.Net;
using Openza.Flow.Core.Services;
using Xunit;

namespace Openza.Flow.Tests;

public sealed class GitHubAuthServiceTests
{
    [Fact]
    public async Task ValidPatStoresTokenAndUsername()
    {
        var tokenStore = new InMemoryTokenStore();
        var service = new GitHubAuthService(
            new HttpClient(new StubHandler(_ => ResponseWithScopes("repo, read:user"))),
            tokenStore);

        var result = await service.ValidateAndSaveTokenAsync("token");

        Assert.True(result.IsValid);
        Assert.Equal("octocat", result.Username);
        Assert.Equal("token", await tokenStore.GetTokenAsync());
        Assert.Equal("octocat", await tokenStore.GetUsernameAsync());
    }

    [Fact]
    public async Task PatWithoutRepoScopeFails()
    {
        var service = new GitHubAuthService(
            new HttpClient(new StubHandler(_ => ResponseWithScopes("read:user"))),
            new InMemoryTokenStore());

        var result = await service.ValidateTokenAsync("token");

        Assert.False(result.IsValid);
        Assert.Contains("repo", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    private static HttpResponseMessage ResponseWithScopes(string scopes)
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"login":"octocat"}""")
        };
        response.Headers.Add("x-oauth-scopes", scopes);
        return response;
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responseFactory;

        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory)
        {
            _responseFactory = responseFactory;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(_responseFactory(request));
        }
    }
}
