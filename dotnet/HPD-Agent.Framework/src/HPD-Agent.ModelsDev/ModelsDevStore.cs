using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace HPD.Agent.ModelsDev;

public enum ModelsDevCatalogOrigin { Network, FreshCache, StaleCache, Embedded, Supplied }
public enum ModelsDevRefreshMode { IfStale, Force, CacheOnly }

public sealed record ModelsDevCatalogSnapshot(
    ModelsDevDatabase Database, DateTimeOffset RetrievedAt, string? ETag,
    string ContentDigest, Uri Source, ModelsDevCatalogOrigin Origin);

public interface IModelsDevCatalog
{
    ValueTask<ModelsDevCatalogSnapshot> GetSnapshotAsync(
        ModelsDevRefreshMode refreshMode = ModelsDevRefreshMode.IfStale,
        CancellationToken cancellationToken = default);
}

public sealed partial class ModelsDevStore : IModelsDevCatalog
{
    private readonly HttpClient _httpClient;
    private readonly ModelsDevOptions _options;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Dictionary<string, string> _payloads = new(StringComparer.Ordinal);
    private readonly ModelsDevCatalogSnapshot? _supplied;
    private ModelsDevCatalogSnapshot? _current;

    public ModelsDevStore(HttpClient httpClient, ModelsDevOptions? options = null)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _options = options ?? new ModelsDevOptions();
    }

    private ModelsDevStore(ModelsDevCatalogSnapshot supplied)
    {
        ValidateDatabase(supplied.Database);
        if (supplied.Origin is not (ModelsDevCatalogOrigin.Supplied or ModelsDevCatalogOrigin.Embedded))
            throw new ArgumentException("A supplied store requires Supplied or Embedded provenance.", nameof(supplied));
        _httpClient = new HttpClient();
        _options = new ModelsDevOptions { UseDiskCache = false, ApiUri = supplied.Source };
        _supplied = _current = supplied;
    }

    public static ModelsDevStore FromSnapshot(ModelsDevCatalogSnapshot snapshot) => new(snapshot);

    public async ValueTask<ModelsDevCatalogSnapshot> GetSnapshotAsync(
        ModelsDevRefreshMode refreshMode = ModelsDevRefreshMode.IfStale,
        CancellationToken cancellationToken = default)
    {
        if (_supplied is not null) return _supplied;
        if (refreshMode is ModelsDevRefreshMode.IfStale && IsFresh(_current)) return _current!;

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (refreshMode is ModelsDevRefreshMode.IfStale && IsFresh(_current)) return _current!;
            var cached = _options.UseDiskCache ? await TryReadCacheAsync(cancellationToken).ConfigureAwait(false) : null;
            if (refreshMode is ModelsDevRefreshMode.IfStale && IsFresh(cached))
                return _current = cached! with { Origin = ModelsDevCatalogOrigin.FreshCache };
            if (refreshMode is ModelsDevRefreshMode.CacheOnly)
                return cached is null
                    ? throw new InvalidOperationException("No valid models.dev cache is available.")
                    : _current = cached with { Origin = IsFresh(cached) ? ModelsDevCatalogOrigin.FreshCache : ModelsDevCatalogOrigin.StaleCache };
            try
            {
                var network = await FetchWithRetriesAsync(cached ?? _current, cancellationToken).ConfigureAwait(false);
                _current = network;
                await TryWriteCacheAsync(network, cancellationToken).ConfigureAwait(false);
                return network;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (Exception exception) when (cached is not null || _current is not null)
            {
                _options.DiagnosticSink?.Invoke(new("stale_cache_fallback", "Catalog refresh failed; using the last valid snapshot.", exception));
                return _current = (cached ?? _current!) with { Origin = ModelsDevCatalogOrigin.StaleCache };
            }
        }
        finally { _gate.Release(); }
    }

    public async ValueTask<ModelsDevModel?> GetModelAsync(
        ModelsDevModelId id, ModelsDevRefreshMode refreshMode = ModelsDevRefreshMode.IfStale,
        CancellationToken cancellationToken = default)
    {
        if (!id.IsValid) return null;
        var database = (await GetSnapshotAsync(refreshMode, cancellationToken).ConfigureAwait(false)).Database;
        if (!database.Providers.TryGetValue(id.Provider, out var provider)) return null;
        if (provider.Models.TryGetValue(id.Model, out var model)) return model;
        return string.Equals(id.Provider, "amazon-bedrock", StringComparison.OrdinalIgnoreCase)
            && TryStripBedrockPrefix(id.Model, out var stripped) && provider.Models.TryGetValue(stripped, out model)
                ? model : null;
    }

    private bool IsFresh(ModelsDevCatalogSnapshot? snapshot)
        => snapshot is not null && DateTimeOffset.UtcNow - snapshot.RetrievedAt < _options.RefreshInterval;

    private async ValueTask<ModelsDevCatalogSnapshot> FetchWithRetriesAsync(
        ModelsDevCatalogSnapshot? previous, CancellationToken cancellationToken)
    {
        Exception? last = null;
        for (var attempt = 0; attempt <= _options.MaxTransientRetries; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try { return await FetchAsync(previous, cancellationToken).ConfigureAwait(false); }
            catch (Exception ex) when (IsTransient(ex))
            {
                if (ex is OperationCanceledException && cancellationToken.IsCancellationRequested) throw;
                last = ex;
                if (attempt == _options.MaxTransientRetries) break;
                var delay = _options.RetryBaseDelay.TotalMilliseconds * Math.Pow(2, attempt)
                    + Random.Shared.NextDouble() * _options.RetryJitter.TotalMilliseconds;
                await Task.Delay(TimeSpan.FromMilliseconds(delay), cancellationToken).ConfigureAwait(false);
            }
        }
        throw new HttpRequestException("models.dev catalog refresh failed after retries.", last);
    }

    private static bool IsTransient(Exception exception) => exception switch
    {
        IOException => true,
        TaskCanceledException => true,
        HttpRequestException { StatusCode: null } => true,
        HttpRequestException { StatusCode: HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests } => true,
        HttpRequestException { StatusCode: >= HttpStatusCode.InternalServerError } => true,
        _ => false
    };

    private async ValueTask<ModelsDevCatalogSnapshot> FetchAsync(
        ModelsDevCatalogSnapshot? previous, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_options.HttpTimeout);
        using var request = new HttpRequestMessage(HttpMethod.Get, _options.ApiUri);
        if (!string.IsNullOrWhiteSpace(previous?.ETag)) request.Headers.IfNoneMatch.ParseAdd(previous.ETag);
        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseContentRead, timeout.Token).ConfigureAwait(false);
        if (response.StatusCode is HttpStatusCode.NotModified && previous is not null)
            return previous with { RetrievedAt = DateTimeOffset.UtcNow, Origin = ModelsDevCatalogOrigin.Network };
        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadAsStringAsync(timeout.Token).ConfigureAwait(false);
        var digest = Digest(payload);
        _payloads[digest] = payload;
        return new(DeserializeAndValidate(payload), DateTimeOffset.UtcNow, response.Headers.ETag?.Tag,
            digest, _options.ApiUri, ModelsDevCatalogOrigin.Network);
    }

    private async ValueTask<ModelsDevCatalogSnapshot?> TryReadCacheAsync(CancellationToken cancellationToken)
    {
        var path = GetCachePath();
        if (path is null || !File.Exists(path)) return null;
        try
        {
            await using var held = await AcquireFileLockAsync(path + ".lock", cancellationToken).ConfigureAwait(false);
            var cached = JsonSerializer.Deserialize(
                await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false),
                ModelsDevJsonContext.Default.ModelsDevCachedData);
            if (cached is null || cached.Source != _options.ApiUri || cached.ContentDigest != Digest(cached.Payload)) return null;
            _payloads[cached.ContentDigest] = cached.Payload;
            return new(DeserializeAndValidate(cached.Payload), cached.RetrievedAt, cached.ETag,
                cached.ContentDigest, cached.Source, ModelsDevCatalogOrigin.FreshCache);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception exception)
        {
            _options.DiagnosticSink?.Invoke(new("cache_read_failed", "The models.dev cache could not be validated.", exception));
            return null;
        }
    }

    private async ValueTask TryWriteCacheAsync(ModelsDevCatalogSnapshot snapshot, CancellationToken cancellationToken)
    {
        var path = GetCachePath();
        if (!_options.UseDiskCache || path is null) return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await using var held = await AcquireFileLockAsync(path + ".lock", cancellationToken).ConfigureAwait(false);
            var payload = _payloads.TryGetValue(snapshot.ContentDigest, out var exactPayload)
                ? exactPayload
                : JsonSerializer.Serialize(snapshot.Database.Providers, ModelsDevJsonContext.Default.DictionaryStringModelsDevProvider);
            var cached = new ModelsDevCachedData
            {
                Payload = payload, RetrievedAt = snapshot.RetrievedAt, ETag = snapshot.ETag,
                ContentDigest = Digest(payload), Source = snapshot.Source
            };
            var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                await File.WriteAllTextAsync(temporary,
                    JsonSerializer.Serialize(cached, ModelsDevJsonContext.Default.ModelsDevCachedData), cancellationToken).ConfigureAwait(false);
                File.Move(temporary, path, true);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception exception)
        {
            _options.DiagnosticSink?.Invoke(new("cache_write_failed", "The valid catalog snapshot could not be written to cache.", exception));
        }
    }

    private static async ValueTask<FileStream> AcquireFileLockAsync(string path, CancellationToken cancellationToken)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try { return new(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
            catch (IOException) { await Task.Delay(25, cancellationToken).ConfigureAwait(false); }
        }
    }

    private string? GetCachePath()
    {
        var basePath = _options.CachePath;
        if (string.IsNullOrWhiteSpace(basePath))
        {
            var profile = System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile);
            if (string.IsNullOrWhiteSpace(profile)) return null;
            basePath = Path.Combine(profile, ".hpd", "models-dev");
        }
        var isFile = !string.IsNullOrEmpty(Path.GetExtension(basePath));
        var directory = isFile ? Path.GetDirectoryName(basePath)! : basePath;
        var prefix = isFile ? Path.GetFileNameWithoutExtension(basePath) : "catalog";
        return Path.Combine(directory, $"{prefix}-{Digest(_options.ApiUri.AbsoluteUri)[..16]}.json");
    }

    private static ModelsDevDatabase DeserializeAndValidate(string payload)
    {
        var providers = JsonSerializer.Deserialize(payload, ModelsDevJsonContext.Default.DictionaryStringModelsDevProvider)
            ?? throw new JsonException("Failed to deserialize models.dev provider catalog.");
        var database = new ModelsDevDatabase { Providers = providers };
        ValidateDatabase(database);
        return database;
    }

    private static void ValidateDatabase(ModelsDevDatabase database)
    {
        foreach (var (providerId, provider) in database.Providers)
        foreach (var (modelId, model) in provider.Models)
        {
            if (model.Cost is null) continue;
            ValidateRates(model.Cost.Input, model.Cost.Output, model.Cost.Reasoning, model.Cost.CacheRead,
                model.Cost.CacheWrite, model.Cost.InputAudio, model.Cost.OutputAudio, providerId, modelId);
            var thresholds = new HashSet<long>();
            foreach (var tier in model.Cost.Tiers)
            {
                if (tier?.Tier is null
                    || !string.Equals(tier.Tier.Type, "context", StringComparison.OrdinalIgnoreCase)
                    || tier.Tier.Size < 0 || !thresholds.Add(tier.Tier.Size))
                    throw new JsonException($"Invalid pricing tier for {providerId}/{modelId}.");
                ValidateRates(tier.Input, tier.Output, tier.Reasoning, tier.CacheRead,
                    tier.CacheWrite, tier.InputAudio, tier.OutputAudio, providerId, modelId);
            }
        }
    }

    private static void ValidateRates(decimal input, decimal output, decimal? reasoning, decimal? cacheRead,
        decimal? cacheWrite, decimal? inputAudio, decimal? outputAudio, string providerId, string modelId)
    {
        if (new decimal?[] { input, output, reasoning, cacheRead, cacheWrite, inputAudio, outputAudio }.Any(rate => rate < 0))
            throw new JsonException($"Negative price for {providerId}/{modelId}.");
    }

    private static string Digest(string value)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    internal static bool TryStripBedrockPrefix(string modelId, out string stripped)
    {
        stripped = modelId;
        var separator = modelId.IndexOf('.');
        if (separator <= 0 || separator == modelId.Length - 1) return false;
        var prefix = modelId[..separator];
        if (prefix is not ("us" or "eu" or "apac" or "global")) return false;
        stripped = modelId[(separator + 1)..];
        return true;
    }
}
