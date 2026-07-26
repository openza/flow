using Openza.Flow.Core.Models;
using Openza.Flow.Core.Services;
using Xunit;

namespace Openza.Flow.Tests;

public sealed class DeveloperProjectUtilitiesTests
{
    [Fact]
    public void AggregateGroupsSessionsByEnvironmentAndRepositoryRoot()
    {
        var windows = Environment("windows", AgentEnvironmentKind.Windows, "Windows");
        var ubuntu = Environment("wsl:ubuntu", AgentEnvironmentKind.Wsl, "Ubuntu");
        var now = DateTimeOffset.UtcNow;

        var projects = DeveloperProjectUtilities.Aggregate(
        [
            Session(windows, "one", @"D:\work\flow\src", now.AddHours(-2), @"D:\work\flow", "main"),
            Session(windows, "two", @"D:\work\flow", now, @"D:\work\flow\", "feature/home"),
            Session(ubuntu, "three", "/home/deependra/flow", now.AddHours(-1), "/home/deependra/flow", "main")
        ]);

        Assert.Equal(2, projects.Count);
        Assert.Equal("windows", projects[0].Environment.Id);
        Assert.Equal(2, projects[0].SessionCount);
        Assert.Equal("feature/home", projects[0].Branch);
        Assert.Equal("two", projects[0].LatestSession.Key.SessionId);
        Assert.Equal("wsl:ubuntu", projects[1].Environment.Id);
    }

    [Fact]
    public void AggregateFallsBackToWorkingDirectoryAndFolderName()
    {
        var environment = Environment("windows", AgentEnvironmentKind.Windows, "Windows");
        var project = Assert.Single(DeveloperProjectUtilities.Aggregate(
        [
            new AgentSessionSummary(
                new AgentSessionKey(environment.Id, "one"),
                "Session",
                @"D:\work\sample",
                DateTimeOffset.UtcNow,
                DateTimeOffset.UtcNow,
                DateTimeOffset.UtcNow,
                "CLI",
                null,
                environment)
        ]));

        Assert.Equal("sample", project.DisplayName);
        Assert.Equal(@"D:\work\sample", project.RootPath);
    }

    [Fact]
    public void MatchesSearchesProjectMetadataCaseInsensitively()
    {
        var environment = Environment("wsl:ubuntu", AgentEnvironmentKind.Wsl, "Ubuntu");
        var project = Assert.Single(DeveloperProjectUtilities.Aggregate(
        [
            Session(environment, "one", "/home/deependra/openza-flow", DateTimeOffset.UtcNow, "/home/deependra/openza-flow", "Feature/Home")
        ]));

        Assert.True(DeveloperProjectUtilities.Matches(project, "ubuntu"));
        Assert.True(DeveloperProjectUtilities.Matches(project, "feature/home"));
        Assert.True(DeveloperProjectUtilities.Matches(project, "OPENZA-FLOW"));
        Assert.False(DeveloperProjectUtilities.Matches(project, "missing"));
    }

    [Fact]
    public void AggregateKeepsCaseDistinctWslRootsSeparate()
    {
        var environment = Environment("wsl:ubuntu", AgentEnvironmentKind.Wsl, "Ubuntu");
        var now = DateTimeOffset.UtcNow;

        var projects = DeveloperProjectUtilities.Aggregate(
        [
            Session(environment, "upper", "/work/Foo", now, "/work/Foo", "main"),
            Session(environment, "lower", "/work/foo", now.AddMinutes(-1), "/work/foo", "main")
        ]);

        Assert.Equal(2, projects.Count);
        Assert.Contains(projects, project => project.RootPath == "/work/Foo");
        Assert.Contains(projects, project => project.RootPath == "/work/foo");
    }

    [Fact]
    public void AggregateGroupsCaseVariantWindowsRoots()
    {
        var environment = Environment("windows", AgentEnvironmentKind.Windows, "Windows");
        var now = DateTimeOffset.UtcNow;

        var project = Assert.Single(DeveloperProjectUtilities.Aggregate(
        [
            Session(environment, "upper", @"D:\Work\Flow", now, @"D:\Work\Flow", "main"),
            Session(environment, "lower", @"d:\work\flow", now.AddMinutes(-1), @"d:\work\flow", "main")
        ]));

        Assert.Equal(2, project.SessionCount);
    }

    private static AgentEnvironment Environment(string id, AgentEnvironmentKind kind, string name) =>
        new(id, kind, name, kind == AgentEnvironmentKind.Wsl ? name : null, "codex", "0.1", AgentEnvironmentAvailability.Available);

    private static AgentSessionSummary Session(
        AgentEnvironment environment,
        string id,
        string workingDirectory,
        DateTimeOffset recency,
        string repositoryRoot,
        string branch) =>
        new(
            new AgentSessionKey(environment.Id, id),
            $"Session {id}",
            workingDirectory,
            recency,
            recency,
            recency,
            "CLI",
            new AgentGitMetadata(branch, repositoryRoot, "https://github.com/openza/flow.git"),
            environment);
}
