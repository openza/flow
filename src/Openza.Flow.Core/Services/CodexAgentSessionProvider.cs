using System.Collections.Concurrent;
using System.Threading.Channels;
using Openza.Flow.Core.Models;

namespace Openza.Flow.Core.Services;

public sealed class CodexAgentSessionProvider : IAgentSessionProvider
{
    private readonly ConcurrentDictionary<string, ICodexAppServerClient> _clients = new(StringComparer.OrdinalIgnoreCase);
    private readonly IAgentEnvironmentDiscovery _discovery;
    private readonly Func<AgentEnvironment, ICodexAppServerClient> _clientFactory;

    public CodexAgentSessionProvider(
        IAgentEnvironmentDiscovery discovery,
        Func<AgentEnvironment, ICodexAppServerClient>? clientFactory = null)
    {
        _discovery = discovery;
        _clientFactory = clientFactory ?? (environment => new CodexAppServerClient(environment));
    }

    public Task<IReadOnlyList<AgentEnvironment>> ProbeEnvironmentsAsync(CancellationToken cancellationToken = default) =>
        _discovery.ProbeAsync(cancellationToken);

    public async IAsyncEnumerable<AgentSessionPage> EnumerateSessionsAsync(
        IReadOnlyCollection<AgentEnvironment> environments,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var enabled = environments.Where(environment => environment.IsAvailable).ToList();
        var channel = Channel.CreateUnbounded<AgentSessionPage>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = enabled.Count == 1
        });

        var producers = enabled.Select(environment => EnumerateEnvironmentAsync(environment, channel.Writer, cancellationToken)).ToList();
        _ = CompleteChannelAsync(producers, channel.Writer);

        await foreach (var page in channel.Reader.ReadAllAsync(cancellationToken))
        {
            yield return page;
        }
    }

    public async Task<AgentSessionPreview> LoadPreviewAsync(AgentSessionSummary session, CancellationToken cancellationToken = default)
    {
        var client = _clients.GetOrAdd(session.Environment.Id, _ => _clientFactory(session.Environment));
        await client.InitializeAsync(cancellationToken);
        return await client.LoadPreviewAsync(session.Key.SessionId, cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var client in _clients.Values)
        {
            await client.DisposeAsync();
        }

        _clients.Clear();
    }

    private async Task EnumerateEnvironmentAsync(
        AgentEnvironment environment,
        ChannelWriter<AgentSessionPage> writer,
        CancellationToken cancellationToken)
    {
        var firstPage = true;
        try
        {
            var client = _clients.GetOrAdd(environment.Id, _ => _clientFactory(environment));
            await client.InitializeAsync(cancellationToken);
            string? cursor = null;
            do
            {
                var page = await client.ListSessionsAsync(cursor, 100, cancellationToken);
                cursor = page.NextCursor;
                await writer.WriteAsync(
                    new AgentSessionPage(environment.Id, page.Sessions, firstPage, cursor is null),
                    cancellationToken);
                firstPage = false;
            }
            while (cursor is not null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (CodexAppServerException exception)
        {
            await writer.WriteAsync(new AgentSessionPage(environment.Id, [], firstPage, true, exception.Category), CancellationToken.None);
        }
        catch (Exception)
        {
            await writer.WriteAsync(new AgentSessionPage(environment.Id, [], firstPage, true, "provider_failure"), CancellationToken.None);
        }
    }

    private static async Task CompleteChannelAsync(IEnumerable<Task> producers, ChannelWriter<AgentSessionPage> writer)
    {
        try
        {
            await Task.WhenAll(producers);
            writer.TryComplete();
        }
        catch (Exception exception)
        {
            writer.TryComplete(exception);
        }
    }
}
