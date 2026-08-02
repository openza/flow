using Openza.Flow.Core.Models;
using Openza.Flow.Core.Services;
using Xunit;

namespace Openza.Flow.Tests;

public sealed class TerminalCommandBuilderTests
{
    [Fact]
    public void WindowsNativeExecutablePreservesMetacharactersAsArgumentData()
    {
        var environment = AgentSessionUtilitiesTests.Environment(
            "codex:windows",
            AgentEnvironmentKind.Windows,
            "Windows") with
        {
            ExecutablePath = @"C:\Users\Dev&User\codex.exe"
        };
        const string sessionId = "safe&%PATH%^\"quoted\"";
        var command = TerminalCommandBuilder.Build(
            AgentSessionUtilitiesTests.Session(
                environment,
                sessionId,
                "Session",
                DateTimeOffset.UtcNow) with
            {
                WorkingDirectory = @"D:\Work Folder"
            },
            TerminalLaunchMode.NewTab);

        Assert.Equal(@"C:\Users\Dev&User\codex.exe", command.Arguments[^3]);
        Assert.Equal("resume", command.Arguments[^2]);
        Assert.Equal(sessionId, command.Arguments[^1]);
        Assert.DoesNotContain("cmd.exe", command.Arguments);
    }

    [Fact]
    public void WindowsNewTabUsesStructuredArgumentsAndOriginalDirectory()
    {
        var environment = AgentSessionUtilitiesTests.Environment("codex:windows", AgentEnvironmentKind.Windows, "Windows") with
        {
            ExecutablePath = "C:\\Users\\User Name\\codex.exe"
        };
        var session = AgentSessionUtilitiesTests.Session(environment, "abc-123", "Session", DateTimeOffset.UtcNow) with
        {
            WorkingDirectory = "C:\\Code Projects\\Flow"
        };

        var command = TerminalCommandBuilder.Build(session, TerminalLaunchMode.NewTab);

        Assert.Equal("wt.exe", command.FileName);
        Assert.Equal([
            "-w", "0", "new-tab", "--startingDirectory", "C:\\Code Projects\\Flow",
            "C:\\Users\\User Name\\codex.exe", "resume", "abc-123"
        ], command.Arguments);
        Assert.DoesNotContain("--dangerously-bypass", command.CopyableCommand, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void WslNewWindowPreservesLinuxUnicodePath()
    {
        var environment = AgentSessionUtilitiesTests.Environment("codex:wsl:ubuntu", AgentEnvironmentKind.Wsl, "Ubuntu") with
        {
            DistributionName = "Ubuntu Dev",
            ExecutablePath = "/home/deependra/.local/bin/codex"
        };
        var session = AgentSessionUtilitiesTests.Session(environment, "xyz", "Session", DateTimeOffset.UtcNow) with
        {
            WorkingDirectory = "/home/deependra/कोड project"
        };

        var command = TerminalCommandBuilder.Build(session, TerminalLaunchMode.NewWindow);

        Assert.Equal([
            "-w", "-1", "new-tab", "wsl.exe", "-d", "Ubuntu Dev", "--cd",
            "/home/deependra/कोड project", "--", "/home/deependra/.local/bin/codex", "resume", "xyz"
        ], command.Arguments);
        Assert.Contains("'/home/deependra/कोड project'", command.CopyableCommand, StringComparison.Ordinal);
    }
}
