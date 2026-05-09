using Openza.Flow.Core.Services;
using Windows.Security.Credentials;
using Windows.Storage;

namespace Openza.Flow.Services;

public sealed class WindowsCredentialTokenStore : ITokenStore
{
    private const string Resource = "Openza.Flow.GitHub";
    private const string TokenUserName = "github_token";
    private const string UsernameKey = "github_username";
    private readonly PasswordVault _vault = new();
    private readonly ApplicationDataContainer _settings = ApplicationData.Current.LocalSettings;

    public Task<string?> GetTokenAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var credential = _vault.Retrieve(Resource, TokenUserName);
            credential.RetrievePassword();
            return Task.FromResult<string?>(credential.Password);
        }
        catch
        {
            return Task.FromResult<string?>(null);
        }
    }

    public Task SaveTokenAsync(string token, CancellationToken cancellationToken = default)
    {
        RemoveToken();
        _vault.Add(new PasswordCredential(Resource, TokenUserName, token));
        return Task.CompletedTask;
    }

    public Task<string?> GetUsernameAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(_settings.Values.TryGetValue(UsernameKey, out var username) ? username as string : null);
    }

    public Task SaveUsernameAsync(string username, CancellationToken cancellationToken = default)
    {
        _settings.Values[UsernameKey] = username;
        return Task.CompletedTask;
    }

    public Task ClearAsync(CancellationToken cancellationToken = default)
    {
        RemoveToken();
        _settings.Values.Remove(UsernameKey);
        return Task.CompletedTask;
    }

    private void RemoveToken()
    {
        try
        {
            _vault.Remove(_vault.Retrieve(Resource, TokenUserName));
        }
        catch
        {
        }
    }
}
