namespace Openza.Flow.Core.Services;

public interface ITokenStore
{
    Task<string?> GetTokenAsync(CancellationToken cancellationToken = default);

    Task SaveTokenAsync(string token, CancellationToken cancellationToken = default);

    Task<string?> GetUsernameAsync(CancellationToken cancellationToken = default);

    Task SaveUsernameAsync(string username, CancellationToken cancellationToken = default);

    Task ClearAsync(CancellationToken cancellationToken = default);
}
