using System.Text.Json;
using Openza.Flow.Core.Models;
using Openza.Flow.Core.Services;
using Xunit;

namespace Openza.Flow.Tests;

public sealed class CodexAppServerParsingTests
{
    [Fact]
    public void SessionPageParsesOpaqueCursorAndMetadata()
    {
        using var document = JsonDocument.Parse("""
            {
              "data": [{
                "id": "session-1",
                "name": "Implement history",
                "cwd": "/work/flow",
                "createdAt": 1784600000,
                "updatedAt": 1784600100,
                "recencyAt": 1784600200,
                "source": "cli",
                "gitInfo": { "branch": "feature/sessions", "repositoryRoot": "/work/flow", "originUrl": "https://example.test/openza/flow.git" }
              }],
              "nextCursor": "opaque-value=="
            }
            """);
        var environment = AgentSessionUtilitiesTests.Environment("codex:wsl:ubuntu", AgentEnvironmentKind.Wsl, "Ubuntu");

        var page = CodexAppServerClient.ParseSessionListPage(document.RootElement, environment);

        Assert.Equal("opaque-value==", page.NextCursor);
        var session = Assert.Single(page.Sessions);
        Assert.Equal("session-1", session.Key.SessionId);
        Assert.Equal("flow", session.Git?.RepositoryName);
    }

    [Fact]
    public void PreviewIncludesOnlyUserAndFinalAssistantMessages()
    {
        using var document = JsonDocument.Parse("""
            {
              "data": [{ "items": [
                { "type": "userMessage", "content": [{ "text": "Please fix it" }] },
                { "type": "reasoning", "text": "private chain" },
                { "type": "commandExecution", "text": "secret output" },
                { "type": "agentMessage", "text": "Interim update" },
                { "type": "agentMessage", "text": "Fixed and verified." },
                { "type": "fileChange", "text": "patch content" }
              ] }]
            }
            """);

        var preview = CodexAppServerClient.ParsePreview(document.RootElement, new AgentSessionKey("env", "id"));

        Assert.Equal(2, preview.Messages.Count);
        Assert.Equal(AgentSessionMessageRole.User, preview.Messages[0].Role);
        Assert.Equal(AgentSessionMessageRole.Assistant, preview.Messages[1].Role);
        Assert.Equal("Fixed and verified.", preview.Messages[1].Text);
        Assert.DoesNotContain(preview.Messages, message => message.Text.Contains("secret", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("cli", "CLI")]
    [InlineData("vscode", "App / IDE")]
    [InlineData("appServer", "Desktop")]
    public void SessionSourceUsesHonestSupportedClassification(string source, string expected)
    {
        using var document = JsonDocument.Parse($$"""
            {
              "data": [{
                "id": "session-1",
                "preview": "Source test",
                "cwd": "C:\\work",
                "createdAt": 1784600000,
                "updatedAt": 1784600100,
                "recencyAt": 1784600200,
                "source": "{{source}}"
              }]
            }
            """);
        var environment = AgentSessionUtilitiesTests.Environment("codex:windows", AgentEnvironmentKind.Windows, "Windows");

        var session = Assert.Single(CodexAppServerClient.ParseSessionListPage(document.RootElement, environment).Sessions);

        Assert.Equal(expected, session.Source);
    }
}
