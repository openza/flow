using System.Net.Http.Headers;
using System.Text.Json;
using Openza.Flow.Core.Models;

namespace Openza.Flow.Core.Services;

public sealed class GitHubAuthService
{
    private readonly HttpClient _httpClient;
    private readonly ITokenStore _tokenStore;

    public GitHubAuthService(HttpClient httpClient, ITokenStore tokenStore)
    {
        _httpClient = httpClient;
        _tokenStore = tokenStore;
    }

    public async Task<DeviceCodeInfo> RequestDeviceCodeAsync(CancellationToken cancellationToken = default)
    {
        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["client_id"] = GitHubConstants.ClientId,
            ["scope"] = GitHubConstants.OAuthScopes
        });

        using var request = new HttpRequestMessage(HttpMethod.Post, GitHubConstants.DeviceCodeUrl)
        {
            Content = content
        };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Failed to request device code: {(int)response.StatusCode}");
        }

        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;
        if (root.TryGetProperty("error", out var error))
        {
            throw new InvalidOperationException(ReadString(root, "error_description") ?? error.GetString() ?? "GitHub returned an OAuth error.");
        }

        return new DeviceCodeInfo(
            ReadRequiredString(root, "device_code"),
            ReadRequiredString(root, "user_code"),
            ReadRequiredString(root, "verification_uri"),
            ReadRequiredInt(root, "expires_in"),
            ReadInt(root, "interval") ?? 5);
    }

    public async Task<OAuthResult> PollForTokenAsync(
        DeviceCodeInfo deviceCodeInfo,
        IProgress<DeviceFlowStatus>? progress = null,
        CancellationToken cancellationToken = default)
    {
        progress?.Report(DeviceFlowStatus.Polling);
        var expiresAt = DateTimeOffset.UtcNow.AddSeconds(deviceCodeInfo.ExpiresIn);
        var interval = Math.Max(1, deviceCodeInfo.Interval);

        while (DateTimeOffset.UtcNow < expiresAt)
        {
            await Task.Delay(TimeSpan.FromSeconds(interval), cancellationToken);

            using var content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["client_id"] = GitHubConstants.ClientId,
                ["device_code"] = deviceCodeInfo.DeviceCode,
                ["grant_type"] = "urn:ietf:params:oauth:grant-type:device_code"
            });

            using var request = new HttpRequestMessage(HttpMethod.Post, GitHubConstants.TokenUrl)
            {
                Content = content
            };
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            using var response = await _httpClient.SendAsync(request, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;

            if (root.TryGetProperty("access_token", out var tokenElement))
            {
                progress?.Report(DeviceFlowStatus.Success);
                var scopes = (ReadString(root, "scope") ?? string.Empty)
                    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                return new OAuthResult(
                    tokenElement.GetString() ?? string.Empty,
                    ReadString(root, "token_type") ?? "bearer",
                    scopes);
            }

            var error = ReadString(root, "error");
            switch (error)
            {
                case "authorization_pending":
                    continue;
                case "slow_down":
                    interval += 5;
                    continue;
                case "expired_token":
                    progress?.Report(DeviceFlowStatus.Expired);
                    throw new TimeoutException("The device code expired. Please try again.");
                case "access_denied":
                    progress?.Report(DeviceFlowStatus.Error);
                    throw new UnauthorizedAccessException("Authorization was denied.");
                case null:
                    progress?.Report(DeviceFlowStatus.Error);
                    throw new InvalidOperationException("GitHub returned an invalid OAuth response.");
                default:
                    progress?.Report(DeviceFlowStatus.Error);
                    throw new InvalidOperationException(ReadString(root, "error_description") ?? error);
            }
        }

        progress?.Report(DeviceFlowStatus.Expired);
        throw new TimeoutException("The device code expired. Please try again.");
    }

    public async Task<TokenValidationResult> ValidateAndSaveTokenAsync(string token, CancellationToken cancellationToken = default)
    {
        var result = await ValidateTokenAsync(token, cancellationToken);
        if (result.IsValid && result.Username is not null)
        {
            await _tokenStore.SaveTokenAsync(token, cancellationToken);
            await _tokenStore.SaveUsernameAsync(result.Username, cancellationToken);
        }

        return result;
    }

    public async Task<TokenValidationResult> ValidateTokenAsync(string token, CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{GitHubConstants.ApiBaseUrl}/user");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        request.Headers.Add("X-GitHub-Api-Version", GitHubConstants.GitHubApiVersion);
        request.Headers.UserAgent.ParseAdd("Openza-Flow");

        try
        {
            using var response = await _httpClient.SendAsync(request, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);

            if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            {
                return TokenValidationResult.Invalid("Invalid token.");
            }

            if (response.StatusCode == System.Net.HttpStatusCode.Forbidden)
            {
                return TokenValidationResult.Invalid("GitHub rejected the request. Check rate limits and token permissions.");
            }

            if (!response.IsSuccessStatusCode)
            {
                return TokenValidationResult.Invalid($"GitHub validation failed with status {(int)response.StatusCode}.");
            }

            using var document = JsonDocument.Parse(body);
            var login = ReadString(document.RootElement, "login");
            if (string.IsNullOrWhiteSpace(login))
            {
                return TokenValidationResult.Invalid("GitHub returned an invalid user response.");
            }

            if (response.Headers.TryGetValues("x-oauth-scopes", out var scopeValues))
            {
                var scopes = string.Join(",", scopeValues)
                    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                if (!scopes.Contains("repo", StringComparer.OrdinalIgnoreCase)
                    && !scopes.Contains("public_repo", StringComparer.OrdinalIgnoreCase))
                {
                    return TokenValidationResult.Invalid("Token is valid but missing repo or public_repo scope.");
                }
            }

            return TokenValidationResult.Valid(login);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            return TokenValidationResult.Invalid($"Connection error: {exception.Message}");
        }
    }

    public async Task SignOutAsync(CancellationToken cancellationToken = default)
    {
        await _tokenStore.ClearAsync(cancellationToken);
    }

    private static string ReadRequiredString(JsonElement element, string propertyName)
    {
        return ReadString(element, propertyName)
            ?? throw new InvalidOperationException($"GitHub response is missing '{propertyName}'.");
    }

    private static int ReadRequiredInt(JsonElement element, string propertyName)
    {
        return ReadInt(element, propertyName)
            ?? throw new InvalidOperationException($"GitHub response is missing '{propertyName}'.");
    }

    private static string? ReadString(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }

    private static int? ReadInt(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var value) && value.TryGetInt32(out var result)
            ? result
            : null;
    }
}
