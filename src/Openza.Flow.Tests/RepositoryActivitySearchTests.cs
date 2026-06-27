using Openza.Flow.Core.Models;
using Openza.Flow.Core.Services;
using Xunit;

namespace Openza.Flow.Tests;

public sealed class RepositoryActivitySearchTests
{
    [Theory]
    [InlineData("openza/flow")]
    [InlineData("v1.2.0")]
    [InlineData("octocat")]
    [InlineData("desktop")]
    public void ReleaseSearchMatchesExpectedFields(string query)
    {
        var release = new GithubRelease(
            1,
            new GithubRepository("openza/flow", "https://github.com/openza/flow"),
            "Desktop release",
            "v1.2.0",
            "https://github.com/openza/flow/releases/tag/v1.2.0",
            "octocat",
            false,
            false,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow);

        Assert.True(RepositoryActivitySearch.MatchesRelease(release, query));
        Assert.False(RepositoryActivitySearch.MatchesRelease(release, "missing"));
    }

    [Theory]
    [InlineData("openza/flow")]
    [InlineData("ci")]
    [InlineData("main")]
    [InlineData("push")]
    [InlineData("success")]
    [InlineData("build")]
    public void WorkflowRunSearchMatchesExpectedFields(string query)
    {
        var run = new GithubWorkflowRun(
            1,
            new GithubRepository("openza/flow", "https://github.com/openza/flow"),
            "CI",
            "Build main",
            "completed",
            "success",
            "push",
            "main",
            "abc123",
            "Build commit",
            42,
            "https://github.com/openza/flow/actions/runs/1",
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow);

        Assert.True(RepositoryActivitySearch.MatchesWorkflowRun(run, query));
        Assert.False(RepositoryActivitySearch.MatchesWorkflowRun(run, "missing"));
    }
}
