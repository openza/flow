using System.Collections.Concurrent;
using System.Threading.Channels;
using Openza.Flow.Core.Models;

namespace Openza.Flow.Core.Services;

public sealed class CodexAgentSessionProvider : IAgentSessionProvider
{
    private readonly ConcurrentDictionary<string, ICodexAppServerClient> _clients = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _clientGate = new(1, 1);
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
        var client = await GetClientAsync(session.Environment, cancellationToken);
        await client.InitializeAsync(cancellationToken);
        return await client.LoadPreviewAsync(session.Key.SessionId, cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        IReadOnlyList<ICodexAppServerClient> clients;
        await _clientGate.WaitAsync();
        try
        {
            clients = _clients.Values.ToList();
            _clients.Clear();
        }
        finally
        {
            _clientGate.Release();
        }

        List<Exception>? failures = null;
        foreach (var client in clients)
        {
            try
            {
                await client.DisposeAsync();
            }
            catch (Exception exception)
            {
                (failures ??= []).Add(exception);
            }
        }

        if (failures is not null)
        {
            throw new AggregateException("One or more Codex app-server clients could not be disposed.", failures);
        }
    }

    private async Task EnumerateEnvironmentAsync(
        AgentEnvironment environment,
        ChannelWriter<AgentSessionPage> writer,
        CancellationToken cancellationToken)
    {
        var firstPage = true;
        try
        {
            var client = await GetClientAsync(environment, cancellationToken);
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

    private async Task<ICodexAppServerClient> GetClientAsync(
        AgentEnvironment environment,
        CancellationToken cancellationToken)
    {
        if (_clients.TryGetValue(environment.Id, out var existing))
        {
            return existing;
        }

        await _clientGate.WaitAsync(cancellationToken);
        try
        {
            if (_clients.TryGetValue(environment.Id, out existing))
            {
                return existing;
            }

            var client = _clientFactory(environment);
            if (_clients.TryAdd(environment.Id, client))
            {
                return client;
            }

            await client.DisposeAsync();
            return _clients[environment.Id];
        }
        finally
        {
            _clientGate.Release();
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
