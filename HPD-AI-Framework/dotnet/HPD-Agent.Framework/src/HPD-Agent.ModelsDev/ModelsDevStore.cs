using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace HPD.Agent.ModelsDev;

public sealed partial class ModelsDevStore
{
    private readonly HttpClient _httpClient;
    private readonly ModelsDevOptions _options;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private ModelsDevDatabase? _database;

    public ModelsDevStore(HttpClient httpClient, ModelsDevOptions? options = null)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _options = options ?? new ModelsDevOptions();
    }

    private ModelsDevStore(ModelsDevDatabase database)
    {
        _httpClient = new HttpClient();
        _options = new ModelsDevOptions { UseDiskCache = false };
        _database = database ?? throw new ArgumentNullException(nameof(database));
    }

    public static ModelsDevStore FromDatabase(ModelsDevDatabase database)
        => new(database);

    public async ValueTask<ModelsDevDatabase> GetDatabaseAsync(CancellationToken cancellationToken = default)
    {
        if (_database is not null)
        {
            return _database;
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_database is not null)
            {
                return _database;
            }

            _database = await LoadDatabaseAsync(cancellationToken);
            return _database;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask<ModelsDevModel?> GetModelAsync(
        ModelsDevModelId id,
        CancellationToken cancellationToken = default)
    {
        if (!id.IsValid)
        {
            return null;
        }

        var database = await GetDatabaseAsync(cancellationToken);
        if (!database.Providers.TryGetValue(id.Provider, out var provider))
        {
            return null;
        }

        if (provider.Models.TryGetValue(id.Model, out var model))
        {
            return model;
        }

        if (string.Equals(id.Provider, "amazon-bedrock", StringComparison.OrdinalIgnoreCase)
            && TryStripBedrockPrefix(id.Model, out var stripped)
            && provider.Models.TryGetValue(stripped, out model))
        {
            return model;
        }

        return null;
    }

    public async ValueTask<string> ResolveModelAliasAsync(
        string providerId,
        string modelId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(providerId) || string.IsNullOrWhiteSpace(modelId))
        {
            return modelId;
        }

        if (DateSuffixRegex().IsMatch(modelId))
        {
            return modelId;
        }

        var database = await GetDatabaseAsync(cancellationToken);
        if (!database.Providers.TryGetValue(providerId, out var provider)
            || !provider.Models.TryGetValue(modelId, out var alias)
            || alias.Name is null
            || !alias.Name.Contains("(latest)", StringComparison.OrdinalIgnoreCase))
        {
            return modelId;
        }

        var baseName = alias.Name.Replace(" (latest)", string.Empty, StringComparison.OrdinalIgnoreCase);
        foreach (var pair in provider.Models)
        {
            if (string.Equals(pair.Key, modelId, StringComparison.OrdinalIgnoreCase)
                || pair.Value.Name is null
                || pair.Value.Name.Contains("(latest)", StringComparison.OrdinalIgnoreCase)
                || !string.Equals(pair.Value.Name, baseName, StringComparison.Ordinal)
                || !DateSuffixRegex().IsMatch(pair.Key))
            {
                continue;
            }

            return pair.Key;
        }

        return modelId;
    }

    private async ValueTask<ModelsDevDatabase> LoadDatabaseAsync(CancellationToken cancellationToken)
    {
        ModelsDevCachedData? cached = null;
        if (_options.UseDiskCache)
        {
            cached = await TryReadCacheAsync(cancellationToken);
            if (cached is not null && DateTimeOffset.UtcNow - cached.LastRefresh < _options.RefreshInterval)
            {
                return cached.Database;
            }
        }

        try
        {
            var fetched = await FetchAsync(cached?.ETag, cancellationToken);
            if (fetched.Database is null && cached is not null)
            {
                await TryWriteCacheAsync(cached.Database, fetched.ETag ?? cached.ETag, cancellationToken);
                return cached.Database;
            }

            if (fetched.Database is null)
            {
                throw new InvalidOperationException("models.dev returned no catalog data.");
            }

            await TryWriteCacheAsync(fetched.Database, fetched.ETag, cancellationToken);
            return fetched.Database;
        }
        catch when (cached is not null)
        {
            return cached.Database;
        }
    }

    private async ValueTask<(ModelsDevDatabase? Database, string? ETag)> FetchAsync(
        string? etag,
        CancellationToken cancellationToken)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(_options.HttpTimeout);

        using var request = new HttpRequestMessage(HttpMethod.Get, _options.ApiUri);
        if (!string.IsNullOrWhiteSpace(etag))
        {
            request.Headers.IfNoneMatch.ParseAdd(etag);
        }

        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cts.Token);
        if (response.StatusCode == HttpStatusCode.NotModified)
        {
            return (null, etag);
        }

        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cts.Token);
        var providers = await JsonSerializer.DeserializeAsync(
            stream,
            ModelsDevJsonContext.Default.DictionaryStringModelsDevProvider,
            cts.Token);

        if (providers is null)
        {
            throw new JsonException("Failed to deserialize models.dev provider catalog.");
        }

        return (new ModelsDevDatabase { Providers = providers }, response.Headers.ETag?.Tag);
    }

    private async ValueTask<ModelsDevCachedData?> TryReadCacheAsync(CancellationToken cancellationToken)
    {
        var path = GetCachePath();
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return null;
        }

        try
        {
            await using var stream = File.OpenRead(path);
            return await JsonSerializer.DeserializeAsync(
                stream,
                ModelsDevJsonContext.Default.ModelsDevCachedData,
                cancellationToken);
        }
        catch
        {
            return null;
        }
    }

    private async ValueTask TryWriteCacheAsync(
        ModelsDevDatabase database,
        string? etag,
        CancellationToken cancellationToken)
    {
        if (!_options.UseDiskCache)
        {
            return;
        }

        var path = GetCachePath();
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        try
        {
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var cached = new ModelsDevCachedData
            {
                Database = database,
                LastRefresh = DateTimeOffset.UtcNow,
                ETag = etag
            };

            await using var stream = File.Create(path);
            await JsonSerializer.SerializeAsync(
                stream,
                cached,
                ModelsDevJsonContext.Default.ModelsDevCachedData,
                cancellationToken);
        }
        catch
        {
            // Cache writes should not prevent model selection.
        }
    }

    private string? GetCachePath()
    {
        if (!string.IsNullOrWhiteSpace(_options.CachePath))
        {
            return _options.CachePath;
        }

        var home = System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile);
        return string.IsNullOrWhiteSpace(home)
            ? null
            : Path.Combine(home, ".hpd", "models_dev.json");
    }

    private static bool TryStripBedrockPrefix(string modelId, out string stripped)
    {
        stripped = modelId;
        var separator = modelId.IndexOf('.');
        if (separator <= 0 || separator == modelId.Length - 1)
        {
            return false;
        }

        var prefix = modelId[..separator];
        if (!string.Equals(prefix, "us", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(prefix, "eu", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(prefix, "apac", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(prefix, "global", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        stripped = modelId[(separator + 1)..];
        return true;
    }

    [GeneratedRegex(@"-\d{4}-?\d{2}-?\d{2}$", RegexOptions.CultureInvariant)]
    private static partial Regex DateSuffixRegex();
}
