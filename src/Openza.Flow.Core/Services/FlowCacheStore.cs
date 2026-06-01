using System.Text.Json;

namespace Openza.Flow.Core.Services;

public interface IFlowCacheStore
{
    Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default);

    Task SetAsync<T>(string key, T value, CancellationToken cancellationToken = default);

    Task ClearAsync(CancellationToken cancellationToken = default);
}

public static class FlowCacheKeys
{
    public const string ReviewRequests = "review_requests";
    public const string CreatedPullRequests = "created_prs";
    public const string ReviewedPullRequests = "reviewed_prs";
    public const string RecentlyCreatedPullRequests = "recently_created_prs";
    public const string Organizations = "organizations";
}

public sealed class FileFlowCacheStore : IFlowCacheStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly string _cacheDirectory;

    public FileFlowCacheStore(string cacheDirectory)
    {
        _cacheDirectory = cacheDirectory;
    }

    public async Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default)
    {
        var path = GetPath(key);
        if (!File.Exists(path))
        {
            return default;
        }

        try
        {
            await using var stream = File.OpenRead(path);
            return await JsonSerializer.DeserializeAsync<T>(stream, JsonOptions, cancellationToken);
        }
        catch (JsonException)
        {
            return default;
        }
        catch (IOException)
        {
            return default;
        }
        catch (UnauthorizedAccessException)
        {
            return default;
        }
    }

    public async Task SetAsync<T>(string key, T value, CancellationToken cancellationToken = default)
    {
        try
        {
            Directory.CreateDirectory(_cacheDirectory);
            var path = GetPath(key);
            var tempPath = $"{path}.{Guid.NewGuid():N}.tmp";

            await using (var stream = File.Create(tempPath))
            {
                await JsonSerializer.SerializeAsync(stream, value, JsonOptions, cancellationToken);
            }

            File.Move(tempPath, path, overwrite: true);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    public Task ClearAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            if (!Directory.Exists(_cacheDirectory))
            {
                return Task.CompletedTask;
            }

            foreach (var file in Directory.EnumerateFiles(_cacheDirectory, "*.json"))
            {
                try
                {
                    File.Delete(file);
                }
                catch (IOException)
                {
                }
                catch (UnauthorizedAccessException)
                {
                }
            }
        }
        catch (UnauthorizedAccessException)
        {
        }

        return Task.CompletedTask;
    }

    private string GetPath(string key)
    {
        var safeName = string.Concat(key.Select(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_' ? ch : '_'));
        return Path.Combine(_cacheDirectory, $"{safeName}.json");
    }
}
