using System.Text;
using Openza.Flow.Core.Models;
using Openza.Flow.Core.Services;
using Xunit;

namespace Openza.Flow.Tests;

public sealed class AgentSessionUtilitiesTests
{
    [Fact]
    public void DecodeWslDistributionListHandlesUtf16AndDeduplicates()
    {
        var bytes = Encoding.Unicode.GetPreamble().Concat(Encoding.Unicode.GetBytes("Ubuntu\r\nDebian\r\nUbuntu\r\n")).ToArray();

        var result = AgentSessionUtilities.DecodeWslDistributionList(bytes);

        Assert.Equal(["Ubuntu", "Debian"], result);
    }

    [Fact]
    public void MergeUsesCompositeEnvironmentAndSessionKey()
    {
        var now = DateTimeOffset.UtcNow;
        var windows = Environment("codex:windows", AgentEnvironmentKind.Windows, "Windows");
        var ubuntu = Environment("codex:wsl:ubuntu", AgentEnvironmentKind.Wsl, "Ubuntu");

        var merged = AgentSessionUtilities.MergeAndSort([
            Session(windows, "same-id", "Windows session", now.AddMinutes(-2)),
            Session(ubuntu, "same-id", "WSL session", now)
        ]);

        Assert.Equal(2, merged.Count);
        Assert.Equal("WSL session", merged[0].Title);
    }

    [Theory]
    [InlineData("FLOW")]
    [InlineData("project")]
    [InlineData("ubuntu")]
    [InlineData("/work/code")]
    [InlineData("cli")]
    public void SearchIsCaseInsensitiveAcrossVisibleFields(string query)
    {
        var environment = Environment("codex:wsl:ubuntu", AgentEnvironmentKind.Wsl, "Ubuntu");
        var session = Session(environment, "id", "Fix Flow", DateTimeOffset.UtcNow) with
        {
            WorkingDirectory = "/work/code",
            Git = new AgentGitMetadata("main", "/work/code", "https://example.test/project.git")
        };

        Assert.True(AgentSessionUtilities.Matches(session, query));
    }

    [Fact]
    public void DateGroupsUseLocalCalendarBoundaries()
    {
        var now = new DateTimeOffset(2026, 7, 23, 12, 0, 0, TimeSpan.Zero);

        Assert.Equal(AgentSessionDateGroup.Today, AgentSessionUtilities.GetDateGroup(now.AddHours(-1), now));
        Assert.Equal(AgentSessionDateGroup.Yesterday, AgentSessionUtilities.GetDateGroup(now.AddDays(-1), now));
        Assert.Equal(AgentSessionDateGroup.ThisWeek, AgentSessionUtilities.GetDateGroup(new DateTimeOffset(2026, 7, 20, 1, 0, 0, TimeSpan.Zero), now));
        Assert.Equal(AgentSessionDateGroup.Older, AgentSessionUtilities.GetDateGroup(now.AddDays(-8), now));
    }

    [Fact]
    public void DisplayTextRepairsCommonUtf8MojibakeWithoutChangingPlainText()
    {
        Assert.Equal(
            "Release – ready… ‘yes’ → done",
            AgentSessionUtilities.NormalizeDisplayText("Release â€“ readyâ€¦ â€˜yesâ€™ â†’ done"));
        Assert.Equal("Plain session title", AgentSessionUtilities.NormalizeDisplayText("Plain session title"));
    }

    internal static AgentEnvironment Environment(string id, AgentEnvironmentKind kind, string name) =>
        new(id, kind, name, kind == AgentEnvironmentKind.Wsl ? name : null, kind == AgentEnvironmentKind.Wsl ? "/usr/bin/codex" : "C:\\Tools\\codex.exe", "0.144.6", AgentEnvironmentAvailability.Available);

    internal static AgentSessionSummary Session(AgentEnvironment environment, string id, string title, DateTimeOffset recency) =>
        new(new AgentSessionKey(environment.Id, id), title, environment.Kind == AgentEnvironmentKind.Wsl ? "/work/code" : "C:\\Work\\Code", recency, recency, recency, "CLI", null, environment);
}
