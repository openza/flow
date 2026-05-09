using Openza.Flow.Core.Services;
using Xunit;

namespace Openza.Flow.Tests;

public sealed class GitHubQueryBuilderTests
{
    [Fact]
    public void ReviewRequestsIncludesUserOrganizationAndSort()
    {
        var query = GitHubQueryBuilder.ReviewRequests("octocat", "openza");

        Assert.Equal("type:pr state:open review-requested:octocat org:openza sort:updated-desc", query);
    }

    [Fact]
    public void SearchAddsPullRequestTypeAndOrganization()
    {
        var query = GitHubQueryBuilder.SearchPullRequests("repo:openza/flow bug", "openza");

        Assert.Equal("repo:openza/flow bug type:pr org:openza", query);
    }

    [Fact]
    public void SearchDoesNotDuplicatePullRequestType()
    {
        var query = GitHubQueryBuilder.SearchPullRequests("type:pr author:octocat", null);

        Assert.Equal("type:pr author:octocat", query);
    }

    [Fact]
    public void SearchReviewRequestsStaysScopedToReviewer()
    {
        var query = GitHubQueryBuilder.SearchReviewRequests("octocat", "repo:openza/flow bug", "openza");

        Assert.Equal("repo:openza/flow bug type:pr state:open review-requested:octocat org:openza", query);
    }

    [Fact]
    public void BroadSearchDoesNotForceReviewRequestScope()
    {
        var query = GitHubQueryBuilder.SearchPullRequests("repo:openza/flow is:closed", null);

        Assert.Equal("repo:openza/flow is:closed type:pr", query);
    }

    [Fact]
    public void SearchCreatedPullRequestsStaysScopedToAuthor()
    {
        var query = GitHubQueryBuilder.SearchCreatedPullRequests("octocat", "docs", null);

        Assert.Equal("docs type:pr state:open author:octocat", query);
    }
}
