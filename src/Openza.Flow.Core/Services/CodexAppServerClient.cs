using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using Openza.Flow.Core.Models;

namespace Openza.Flow.Core.Services;

public sealed class CodexAppServerException(string category, string message, Exception? innerException = null)
    : Exception(message, innerException)
{
    public string Category { get; } = category;
}

internal sealed class AsyncInitializationGate : IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private int _initialized;

    public bool IsInitialized => Volatile.Read(ref _initialized) != 0;

    public async Task EnsureInitializedAsync(
        Func<CancellationToken, Task> initializeAsync,
        CancellationToken cancellationToken)
    {
        if (Volatile.Read(ref _initialized) != 0)
        {
            return;
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_initialized != 0)
            {
                return;
            }

            await initializeAsync(cancellationToken);
            Volatile.Write(ref _initialized, 1);
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose() => _gate.Dispose();
}

public sealed class CodexAppServerClient : ICodexAppServerClient
{
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(20);
    private readonly ConcurrentDictionary<long, TaskCompletionSource<JsonElement>> _pending = new();
    private readonly SemaphoreSlim _writeGate = new(1, 1);
    private readonly AsyncInitializationGate _initializationGate = new();
    private readonly Process _process;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly Task _readTask;
    private readonly Task _stderrTask;
    private long _nextRequestId;
    private int _disposeState;

    public CodexAppServerClient(AgentEnvironment environment)
    {
        if (!environment.IsAvailable || string.IsNullOrWhiteSpace(environment.ExecutablePath))
        {
            throw new ArgumentException("A working Codex environment is required.", nameof(environment));
        }

        Environment = environment;
        _process = CreateProcess(environment);
        try
        {
            if (!_process.Start())
            {
                throw new CodexAppServerException("process_start", "Codex app-server could not be started.");
            }
        }
        catch (CodexAppServerException)
        {
            CleanupFailedStart();
            throw;
        }
        catch (Exception exception)
        {
            CleanupFailedStart();
            throw new CodexAppServerException("process_start", "Codex app-server could not be started.", exception);
        }

        _readTask = ReadLoopAsync(_lifetime.Token);
        _stderrTask = DrainStderrAsync(_lifetime.Token);
    }

    public AgentEnvironment Environment { get; }

    public Task InitializeAsync(CancellationToken cancellationToken = default) =>
        _initializationGate.EnsureInitializedAsync(
            async token =>
            {
                await RequestAsync(
                    "initialize",
                    new JsonObject
                    {
                        ["clientInfo"] = new JsonObject
                        {
                            ["name"] = "openza-flow",
                            ["title"] = "Openza Flow",
                            ["version"] = "1.0.0"
                        },
                        ["capabilities"] = new JsonObject { ["experimentalApi"] = true }
                    },
                    token);
                await SendNotificationAsync("initialized", new JsonObject(), token);
            },
            cancellationToken);

    public async Task<CodexSessionListPage> ListSessionsAsync(string? cursor, int limit, CancellationToken cancellationToken = default)
    {
        EnsureInitialized();
        var parameters = new JsonObject
        {
            ["limit"] = Math.Clamp(limit, 1, 100),
            ["sortKey"] = "recency_at",
            ["sortDirection"] = "desc",
            ["modelProviders"] = new JsonArray(),
            ["sourceKinds"] = new JsonArray(),
            ["archived"] = false,
            ["useStateDbOnly"] = true
        };
        if (!string.IsNullOrWhiteSpace(cursor))
        {
            parameters["cursor"] = cursor;
        }

        var result = await RequestAsync("thread/list", parameters, cancellationToken);
        return ParseSessionListPage(result, Environment);
    }

    public async Task<AgentSessionPreview> LoadPreviewAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        EnsureInitialized();
        try
        {
            var result = await RequestAsync(
                "thread/turns/list",
                new JsonObject
                {
                    ["threadId"] = sessionId,
                    ["limit"] = 3,
                    ["sortDirection"] = "desc",
                    ["itemsView"] = "summary"
                },
                cancellationToken);
            return ParsePreview(result, new AgentSessionKey(Environment.Id, sessionId));
        }
        catch (CodexAppServerException exception) when (exception.Category is "method_not_found" or "invalid_params")
        {
            return AgentSessionPreview.Unavailable(
                new AgentSessionKey(Environment.Id, sessionId),
                "Preview is unavailable with this Codex version.");
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposeState, 1) != 0)
        {
            return;
        }

        _lifetime.Cancel();
        try
        {
            if (!_process.HasExited)
            {
                _process.StandardInput.Close();
                using var grace = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                await _process.WaitForExitAsync(grace.Token);
            }
        }
        catch
        {
            TryKill();
        }

        TryKill();
        try
        {
            await Task.WhenAll(_readTask, _stderrTask);
        }
        catch
        {
            // Disposal is best effort.
        }

        _process.Dispose();
        _initializationGate.Dispose();
        _writeGate.Dispose();
        _lifetime.Dispose();
    }

    private void CleanupFailedStart()
    {
        _process.Dispose();
        _initializationGate.Dispose();
        _writeGate.Dispose();
        _lifetime.Dispose();
    }

    internal static CodexSessionListPage ParseSessionListPage(JsonElement result, AgentEnvironment environment)
    {
        var sessions = new List<AgentSessionSummary>();
        if (TryGetArray(result, out var data, "data", "threads"))
        {
            foreach (var thread in data.EnumerateArray())
            {
                var sessionId = GetString(thread, "id", "sessionId");
                if (string.IsNullOrWhiteSpace(sessionId))
                {
                    continue;
                }

                var preview = AgentSessionUtilities.NormalizeDisplayText(GetString(thread, "name", "preview") ?? "Untitled session");
                var cwd = GetString(thread, "cwd") ?? string.Empty;
                var created = GetDate(thread, "createdAt");
                var updated = GetDate(thread, "updatedAt");
                var recency = GetDate(thread, "recencyAt");
                if (recency == default)
                {
                    recency = updated == default ? created : updated;
                }

                var source = GetSource(thread);
                AgentGitMetadata? git = null;
                if (thread.TryGetProperty("gitInfo", out var gitInfo) && gitInfo.ValueKind == JsonValueKind.Object)
                {
                    git = new AgentGitMetadata(
                        GetString(gitInfo, "branch"),
                        GetString(gitInfo, "repositoryRoot", "root"),
                        GetString(gitInfo, "remoteUrl", "originUrl"));
                }

                sessions.Add(new AgentSessionSummary(
                    new AgentSessionKey(environment.Id, sessionId),
                    preview,
                    cwd,
                    created,
                    updated,
                    recency,
                    source,
                    git,
                    environment));
            }
        }

        var nextCursor = GetString(result, "nextCursor");
        return new CodexSessionListPage(sessions, nextCursor);
    }

    internal static AgentSessionPreview ParsePreview(JsonElement result, AgentSessionKey key)
    {
        var turns = new List<JsonElement>();
        if (TryGetArray(result, out var data, "data", "turns"))
        {
            turns.AddRange(data.EnumerateArray().Select(turn => turn.Clone()));
        }

        turns.Reverse();
        var messages = new List<AgentSessionMessage>();
        foreach (var turn in turns)
        {
            if (!TryGetArray(turn, out var items, "items"))
            {
                continue;
            }

            AgentSessionMessage? finalAssistantMessage = null;
            foreach (var item in items.EnumerateArray())
            {
                var type = GetString(item, "type")?.ToLowerInvariant();
                var role = type switch
                {
                    "usermessage" or "user_message" => AgentSessionMessageRole.User,
                    "agentmessage" or "assistantmessage" or "agent_message" or "assistant_message" => AgentSessionMessageRole.Assistant,
                    _ => (AgentSessionMessageRole?)null
                };
                if (role is null)
                {
                    continue;
                }

                var text = GetMessageText(item);
                if (!string.IsNullOrWhiteSpace(text))
                {
                    var message = new AgentSessionMessage(role.Value, AgentSessionUtilities.NormalizeDisplayText(text));
                    if (role == AgentSessionMessageRole.User)
                    {
                        messages.Add(message);
                    }
                    else
                    {
                        finalAssistantMessage = message;
                    }
                }
            }

            if (finalAssistantMessage is not null)
            {
                messages.Add(finalAssistantMessage);
            }
        }

        return new AgentSessionPreview(key, messages);
    }

    private async Task<JsonElement> RequestAsync(string method, JsonObject parameters, CancellationToken cancellationToken)
    {
        var id = Interlocked.Increment(ref _nextRequestId);
        var completion = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_pending.TryAdd(id, completion))
        {
            throw new CodexAppServerException("correlation", "A request identifier collision occurred.");
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
        timeout.CancelAfter(RequestTimeout);
        try
        {
            await WriteAsync(
                new JsonObject
                {
                    ["id"] = id,
                    ["method"] = method,
                    ["params"] = parameters
                },
                timeout.Token);
            return await completion.Task.WaitAsync(timeout.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && !_lifetime.IsCancellationRequested)
        {
            throw new CodexAppServerException("timeout", "Codex app-server did not respond in time.");
        }
        finally
        {
            _pending.TryRemove(id, out _);
        }
    }

    private Task SendNotificationAsync(string method, JsonObject parameters, CancellationToken cancellationToken) =>
        WriteAsync(new JsonObject { ["method"] = method, ["params"] = parameters }, cancellationToken);

    private async Task WriteAsync(JsonObject message, CancellationToken cancellationToken)
    {
        await _writeGate.WaitAsync(cancellationToken);
        try
        {
            await _process.StandardInput.WriteLineAsync(message.ToJsonString().AsMemory(), cancellationToken);
            await _process.StandardInput.FlushAsync(cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw new CodexAppServerException("write", "Codex app-server input is unavailable.", exception);
        }
        finally
        {
            _writeGate.Release();
        }
    }

    private async Task ReadLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var line = await _process.StandardOutput.ReadLineAsync(cancellationToken);
                if (line is null)
                {
                    FailPending(new CodexAppServerException("eof", "Codex app-server closed its output stream."));
                    return;
                }

                if (!TryParseResponseLine(line, out var document))
                {
                    // Ignore non-protocol stdout. Correlated requests retain their own timeout.
                    continue;
                }

                using (document)
                {
                    var root = document.RootElement;
                    if (!root.TryGetProperty("id", out var idElement) || !idElement.TryGetInt64(out var id) || !_pending.TryGetValue(id, out var completion))
                    {
                        continue;
                    }

                    if (root.TryGetProperty("error", out var error))
                    {
                        var code = error.TryGetProperty("code", out var codeElement) && codeElement.TryGetInt32(out var number) ? number : 0;
                        var category = code == -32601 ? "method_not_found" : code == -32602 ? "invalid_params" : "protocol_error";
                        completion.TrySetException(new CodexAppServerException(category, "Codex app-server rejected the request."));
                    }
                    else if (root.TryGetProperty("result", out var result))
                    {
                        completion.TrySetResult(result.Clone());
                    }
                    else
                    {
                        completion.TrySetException(new CodexAppServerException("protocol_error", "Codex app-server returned an incomplete response."));
                    }
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            FailPending(new CodexAppServerException("read", "Codex app-server output is unavailable.", exception));
        }
    }

    internal static bool TryParseResponseLine(string line, out JsonDocument document)
    {
        try
        {
            document = JsonDocument.Parse(line);
            return true;
        }
        catch (JsonException)
        {
            document = null!;
            return false;
        }
    }

    private async Task DrainStderrAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (await _process.StandardError.ReadLineAsync(cancellationToken) is not null)
            {
                // Deliberately discard content. It may contain session data or paths.
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private static Process CreateProcess(AgentEnvironment environment)
    {
        var startInfo = new ProcessStartInfo
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        if (environment.Kind == AgentEnvironmentKind.Windows)
        {
            startInfo.FileName = environment.ExecutablePath!;
            startInfo.ArgumentList.Add("app-server");
            startInfo.ArgumentList.Add("--stdio");
        }
        else
        {
            startInfo.FileName = "wsl.exe";
            startInfo.ArgumentList.Add("-d");
            startInfo.ArgumentList.Add(environment.DistributionName!);
            startInfo.ArgumentList.Add("--");
            startInfo.ArgumentList.Add(environment.ExecutablePath!);
            startInfo.ArgumentList.Add("app-server");
            startInfo.ArgumentList.Add("--stdio");
        }

        return new Process { StartInfo = startInfo, EnableRaisingEvents = true };
    }

    private static bool TryGetArray(JsonElement element, out JsonElement array, params string[] names)
    {
        foreach (var name in names)
        {
            if (element.TryGetProperty(name, out array) && array.ValueKind == JsonValueKind.Array)
            {
                return true;
            }
        }

        array = default;
        return false;
    }

    private static string? GetString(JsonElement element, params string[] names)
    {
        foreach (var name in names)
        {
            if (element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String)
            {
                var text = value.GetString();
                if (!string.IsNullOrWhiteSpace(text))
                {
                    return text;
                }
            }
        }

        return null;
    }

    private static DateTimeOffset GetDate(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value))
        {
            return default;
        }

        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var seconds))
        {
            return DateTimeOffset.FromUnixTimeSeconds(seconds);
        }

        return value.ValueKind == JsonValueKind.String && DateTimeOffset.TryParse(value.GetString(), out var date) ? date : default;
    }

    private static string GetSource(JsonElement thread)
    {
        var source = GetString(thread, "source", "threadSource");
        if (source is not null)
        {
            return NormalizeSource(source);
        }

        foreach (var propertyName in new[] { "source", "threadSource" })
        {
            if (thread.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.Object)
            {
                var kind = value.EnumerateObject().FirstOrDefault().Name;
                if (!string.IsNullOrWhiteSpace(kind))
                {
                    return NormalizeSource(kind);
                }
            }
        }

        return "CLI";
    }

    private static string NormalizeSource(string source) => source.ToLowerInvariant() switch
    {
        // Codex Desktop currently persists many of its sessions as `vscode` and
        // thread/list does not expose the rollout originator needed to split them.
        var value when value.Contains("vscode", StringComparison.Ordinal) || value.Contains("ide", StringComparison.Ordinal) => "App / IDE",
        var value when value.Contains("desktop", StringComparison.Ordinal) || value.Contains("appserver", StringComparison.Ordinal) => "Desktop",
        _ => "CLI"
    };

    private static string? GetMessageText(JsonElement item)
    {
        var direct = GetString(item, "text", "message");
        if (direct is not null)
        {
            return direct;
        }

        if (!item.TryGetProperty("content", out var content))
        {
            return null;
        }

        if (content.ValueKind == JsonValueKind.String)
        {
            return content.GetString();
        }

        if (content.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var parts = content.EnumerateArray()
            .Select(part => part.ValueKind == JsonValueKind.String ? part.GetString() : GetString(part, "text"))
            .Where(text => !string.IsNullOrWhiteSpace(text));
        return string.Join(System.Environment.NewLine, parts!);
    }

    private void EnsureInitialized()
    {
        if (!_initializationGate.IsInitialized)
        {
            throw new InvalidOperationException("Initialize the app-server client before sending requests.");
        }
    }

    private void FailPending(Exception exception)
    {
        foreach (var completion in _pending.Values)
        {
            completion.TrySetException(exception);
        }
    }

    private void TryKill()
    {
        try
        {
            if (!_process.HasExited)
            {
                _process.Kill(entireProcessTree: true);
            }
        }
        catch
        {
            // Best effort cleanup only.
        }
    }
}
