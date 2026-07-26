using Openza.Flow.Core.Models;

namespace Openza.Flow.Core.Services;

public interface IAgentSessionProvider : IAsyncDisposable
{
    Task<IReadOnlyList<AgentEnvironment>> ProbeEnvironmentsAsync(CancellationToken cancellationToken = default);

    IAsyncEnumerable<AgentSessionPage> EnumerateSessionsAsync(
        IReadOnlyCollection<AgentEnvironment> environments,
        CancellationToken cancellationToken = default);

    Task<AgentSessionPreview> LoadPreviewAsync(
        AgentSessionSummary session,
        CancellationToken cancellationToken = default);
}

public sealed record AgentSessionPage(
    string EnvironmentId,
    IReadOnlyList<AgentSessionSummary> Sessions,
    bool IsFirstPage,
    bool IsLastPage,
    string? ErrorCategory = null);

public interface ICodexAppServerClient : IAsyncDisposable
{
    AgentEnvironment Environment { get; }

    Task InitializeAsync(CancellationToken cancellationToken = default);

    Task<CodexSessionListPage> ListSessionsAsync(string? cursor, int limit, CancellationToken cancellationToken = default);

    Task<AgentSessionPreview> LoadPreviewAsync(string sessionId, CancellationToken cancellationToken = default);
}

public sealed record CodexSessionListPage(IReadOnlyList<AgentSessionSummary> Sessions, string? NextCursor);

public interface ITerminalLauncher
{
    Task<TerminalLaunchValidation> ValidateAsync(AgentSessionSummary session, CancellationToken cancellationToken = default);

    TerminalLaunchCommand BuildCommand(AgentSessionSummary session, TerminalLaunchMode mode);

    Task LaunchAsync(AgentSessionSummary session, TerminalLaunchMode mode, CancellationToken cancellationToken = default);
}

public sealed record TerminalLaunchValidation(bool IsValid, string? ErrorCategory = null, string? Message = null);
