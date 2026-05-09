namespace Openza.Flow.Core.Services;

public sealed class InMemoryTokenStore : ITokenStore
{
    private string? _token;
    private string? _username;

    public Task<string?> GetTokenAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(_token);
    }

    public Task SaveTokenAsync(string token, CancellationToken cancellationToken = default)
    {
        _token = token;
        return Task.CompletedTask;
    }

    public Task<string?> GetUsernameAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(_username);
    }

    public Task SaveUsernameAsync(string username, CancellationToken cancellationToken = default)
    {
        _username = username;
        return Task.CompletedTask;
    }

    public Task ClearAsync(CancellationToken cancellationToken = default)
    {
        _token = null;
        _username = null;
        return Task.CompletedTask;
    }
}
