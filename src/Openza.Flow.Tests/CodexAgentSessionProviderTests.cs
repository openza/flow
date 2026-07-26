using Openza.Flow.Core.Models;
using Openza.Flow.Core.Services;
using Xunit;

namespace Openza.Flow.Tests;

public sealed class CodexAgentSessionProviderTests
{
    [Fact]
    public async Task EnumerationPublishesEveryOpaqueCursorPage()
    {
        var environment = AgentSessionUtilitiesTests.Environment("codex:wsl:ubuntu", AgentEnvironmentKind.Wsl, "Ubuntu");
        var client = new FakeClient(environment,
        [
            new CodexSessionListPage([AgentSessionUtilitiesTests.Session(environment, "one", "One", DateTimeOffset.UtcNow)], "opaque=="),
            new CodexSessionListPage([AgentSessionUtilitiesTests.Session(environment, "two", "Two", DateTimeOffset.UtcNow.AddMinutes(-1))], null)
        ]);
        await using var provider = new CodexAgentSessionProvider(new FakeDiscovery([environment]), _ => client);

        var pages = new List<AgentSessionPage>();
        await foreach (var page in provider.EnumerateSessionsAsync([environment]))
        {
            pages.Add(page);
        }

        Assert.Equal(2, pages.Count);
        Assert.True(pages[0].IsFirstPage);
        Assert.False(pages[0].IsLastPage);
        Assert.True(pages[1].IsLastPage);
        Assert.Equal([null, "opaque=="], client.ReceivedCursors);
    }

    [Fact]
    public async Task ProviderFailureIsIsolatedFromOtherEnvironment()
    {
        var windows = AgentSessionUtilitiesTests.Environment("codex:windows", AgentEnvironmentKind.Windows, "Windows");
        var ubuntu = AgentSessionUtilitiesTests.Environment("codex:wsl:ubuntu", AgentEnvironmentKind.Wsl, "Ubuntu");
        var working = new FakeClient(windows,
        [
            new CodexSessionListPage([AgentSessionUtilitiesTests.Session(windows, "one", "One", DateTimeOffset.UtcNow)], null)
        ]);
        var failing = new FakeClient(ubuntu, [], new CodexAppServerException("process_exit", "Sanitized failure."));
        await using var provider = new CodexAgentSessionProvider(
            new FakeDiscovery([windows, ubuntu]),
            environment => environment.Id == windows.Id ? working : failing);

        var pages = new List<AgentSessionPage>();
        await foreach (var page in provider.EnumerateSessionsAsync([windows, ubuntu]))
        {
            pages.Add(page);
        }

        Assert.Contains(pages, page => page.EnvironmentId == windows.Id && page.Sessions.Count == 1);
        Assert.Contains(pages, page => page.EnvironmentId == ubuntu.Id && page.ErrorCategory == "process_exit");
    }

    [Fact]
    public async Task PreviewReconnectsEnvironmentAfterProviderWasDisposed()
    {
        var environment = AgentSessionUtilitiesTests.Environment("codex:wsl:ubuntu", AgentEnvironmentKind.Wsl, "Ubuntu");
        var session = AgentSessionUtilitiesTests.Session(environment, "one", "One", DateTimeOffset.UtcNow);
        var clients = new List<FakeClient>();
        await using var provider = new CodexAgentSessionProvider(
            new FakeDiscovery([environment]),
            _ =>
            {
                var client = new FakeClient(environment, [new CodexSessionListPage([session], null)]);
                clients.Add(client);
                return client;
            });

        await foreach (var _ in provider.EnumerateSessionsAsync([environment]))
        {
        }
        await provider.DisposeAsync();

        var preview = await provider.LoadPreviewAsync(session);

        Assert.True(preview.IsAvailable);
        Assert.Equal(2, clients.Count);
        Assert.Equal(1, clients[1].InitializeCallCount);
    }

    private sealed class FakeDiscovery(IReadOnlyList<AgentEnvironment> environments) : IAgentEnvironmentDiscovery
    {
        public Task<IReadOnlyList<AgentEnvironment>> ProbeAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(environments);
    }

    private sealed class FakeClient(
        AgentEnvironment environment,
        IReadOnlyList<CodexSessionListPage> pages,
        Exception? initializationFailure = null) : ICodexAppServerClient
    {
        private int _page;

        public AgentEnvironment Environment { get; } = environment;
        public List<string?> ReceivedCursors { get; } = [];
        public int InitializeCallCount { get; private set; }

        public Task InitializeAsync(CancellationToken cancellationToken = default)
        {
            InitializeCallCount++;
            return initializationFailure is null ? Task.CompletedTask : Task.FromException(initializationFailure);
        }

        public Task<CodexSessionListPage> ListSessionsAsync(string? cursor, int limit, CancellationToken cancellationToken = default)
        {
            ReceivedCursors.Add(cursor);
            return Task.FromResult(pages[_page++]);
        }

        public Task<AgentSessionPreview> LoadPreviewAsync(string sessionId, CancellationToken cancellationToken = default) =>
            Task.FromResult(new AgentSessionPreview(new AgentSessionKey(Environment.Id, sessionId), []));

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
