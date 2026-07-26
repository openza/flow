using System.Runtime.CompilerServices;
using Openza.Flow.Core.Models;
using Openza.Flow.Core.Services;
using Xunit;

namespace Openza.Flow.Tests;

public sealed class AgentSessionWorkspaceTests
{
    [Fact]
    public async Task ConcurrentRefreshUsesSingleProviderEnumeration()
    {
        var provider = new FakeProvider();
        await using var workspace = new AgentSessionWorkspace(provider, new Enablement());

        await Task.WhenAll(workspace.RefreshAsync(), workspace.RefreshAsync(), workspace.RefreshAsync());

        Assert.Equal(1, provider.ProbeCount);
        Assert.Equal(1, provider.EnumerationCount);
        Assert.Single(workspace.Sessions);
    }

    [Fact]
    public async Task EnsureFreshReusesRecentSnapshot()
    {
        var provider = new FakeProvider();
        await using var workspace = new AgentSessionWorkspace(provider, new Enablement());

        await workspace.RefreshAsync();
        await workspace.EnsureFreshAsync(TimeSpan.FromMinutes(5));

        Assert.Equal(1, provider.ProbeCount);
    }

    [Fact]
    public async Task PartialFailureKeepsSuccessfulEnvironment()
    {
        var provider = new FakeProvider(includeFailure: true);
        await using var workspace = new AgentSessionWorkspace(provider, new Enablement());

        await workspace.RefreshAsync();

        Assert.Equal(AgentSessionWorkspaceState.PartialFailure, workspace.State);
        Assert.Single(workspace.Sessions);
        Assert.Contains("1 of 2", workspace.StatusMessage);
    }

    [Fact]
    public async Task DisablingLastEnvironmentClearsExistingSessions()
    {
        var provider = new FakeProvider();
        var enablement = new MutableEnablement();
        await using var workspace = new AgentSessionWorkspace(provider, enablement);

        await workspace.RefreshAsync();
        Assert.Single(workspace.Sessions);

        enablement.IsEnabled = false;
        await workspace.RefreshAsync();

        Assert.Empty(workspace.Sessions);
        Assert.Contains("No coding agent environments are enabled", workspace.StatusMessage);
        Assert.Equal(1, provider.EnumerationCount);
    }

    private sealed class Enablement : IAgentEnvironmentEnablement
    {
        public bool IsAgentEnvironmentEnabled(string environmentId) => true;
    }

    private sealed class MutableEnablement : IAgentEnvironmentEnablement
    {
        public bool IsEnabled { get; set; } = true;

        public bool IsAgentEnvironmentEnabled(string environmentId) => IsEnabled;
    }

    private sealed class FakeProvider(bool includeFailure = false) : IAgentSessionProvider
    {
        private readonly AgentEnvironment _windows = new(
            "windows",
            AgentEnvironmentKind.Windows,
            "Windows",
            null,
            "codex.exe",
            "0.1",
            AgentEnvironmentAvailability.Available);

        public int ProbeCount { get; private set; }

        public int EnumerationCount { get; private set; }

        public Task<IReadOnlyList<AgentEnvironment>> ProbeEnvironmentsAsync(CancellationToken cancellationToken = default)
        {
            ProbeCount++;
            IReadOnlyList<AgentEnvironment> environments = includeFailure
                ? [_windows, _windows with { Id = "wsl:ubuntu", Kind = AgentEnvironmentKind.Wsl, DisplayName = "Ubuntu", DistributionName = "Ubuntu" }]
                : [_windows];
            return Task.FromResult(environments);
        }

        public async IAsyncEnumerable<AgentSessionPage> EnumerateSessionsAsync(
            IReadOnlyCollection<AgentEnvironment> environments,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            EnumerationCount++;
            await Task.Yield();
            var now = DateTimeOffset.UtcNow;
            yield return new AgentSessionPage(
                _windows.Id,
                [
                    new AgentSessionSummary(
                        new AgentSessionKey(_windows.Id, "one"),
                        "Session",
                        @"D:\work",
                        now,
                        now,
                        now,
                        "CLI",
                        null,
                        _windows)
                ],
                true,
                true);
            if (includeFailure)
            {
                yield return new AgentSessionPage("wsl:ubuntu", [], true, true, "timeout");
            }
        }

        public Task<AgentSessionPreview> LoadPreviewAsync(
            AgentSessionSummary session,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new AgentSessionPreview(session.Key, []));

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
