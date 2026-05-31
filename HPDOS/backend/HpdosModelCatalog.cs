using System.Text.Json;

internal sealed class HpdosModelCatalogService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly IReadOnlyDictionary<string, string> ModelsDevProviderByHpdosProvider =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["anthropic"] = "anthropic",
            ["google-ai"] = "google",
            ["huggingface"] = "huggingface",
            ["mistral"] = "mistral",
            ["openai"] = "openai",
            ["openrouter"] = "openrouter"
        };

    private readonly string snapshotPath;
    private readonly string recommendationsPath;
    private readonly HpdosCustomModelStore customModels;
    private readonly Lazy<IReadOnlyDictionary<string, HpdosModelsDevProviderSnapshot>> modelsDevProviders;

    public HpdosModelCatalogService(string backendDirectory, HpdosCustomModelStore customModels)
    {
        snapshotPath = Path.Combine(backendDirectory, "models-dev.snapshot.json");
        recommendationsPath = Path.Combine(backendDirectory, "model-recommendations.json");
        this.customModels = customModels;
        modelsDevProviders = new Lazy<IReadOnlyDictionary<string, HpdosModelsDevProviderSnapshot>>(LoadModelsDevProviders);
    }

    public async Task<List<HpdosModelCatalogItem>> ListModelsAsync(
        IReadOnlySet<string> registeredProviders,
        CancellationToken ct)
    {
        var models = new List<HpdosModelCatalogItem>();

        foreach (var providerKey in registeredProviders)
        {
            if (ModelsDevProviderByHpdosProvider.TryGetValue(providerKey, out var modelsDevProviderKey)
                && modelsDevProviders.Value.TryGetValue(modelsDevProviderKey, out var providerSnapshot))
            {
                models.AddRange(providerSnapshot.Models.Select(model => ToCatalogItem(providerKey, model)));
            }
        }

        models.AddRange((await customModels.ListAsync(ct))
            .Where(model => registeredProviders.Contains(model.ProviderKey))
            .Select(ToCatalogItem));

        var recommendedModels = LoadRecommendedModels();

        return models
            .GroupBy(model => $"{model.ProviderKey}:{model.ModelId}", StringComparer.OrdinalIgnoreCase)
            .Select(group => group.Last())
            .Select(model => model with { Recommended = recommendedModels.Contains(ModelKey(model.ProviderKey, model.ModelId)) })
            .OrderBy(model => model.ProviderKey, StringComparer.OrdinalIgnoreCase)
            .ThenBy(model => model.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private IReadOnlyDictionary<string, HpdosModelsDevProviderSnapshot> LoadModelsDevProviders()
    {
        if (!File.Exists(snapshotPath))
            return new Dictionary<string, HpdosModelsDevProviderSnapshot>(StringComparer.OrdinalIgnoreCase);

        using var stream = File.OpenRead(snapshotPath);
        var snapshot = JsonSerializer.Deserialize<HpdosModelsDevSnapshot>(stream, JsonOptions);
        if (snapshot?.Providers is null)
            return new Dictionary<string, HpdosModelsDevProviderSnapshot>(StringComparer.OrdinalIgnoreCase);

        return snapshot.Providers.ToDictionary(provider => provider.ProviderId, StringComparer.OrdinalIgnoreCase);
    }

    private HashSet<string> LoadRecommendedModels()
    {
        if (!File.Exists(recommendationsPath))
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            using var stream = File.OpenRead(recommendationsPath);
            var recommendations = JsonSerializer.Deserialize<HpdosRecommendedModelStoreFile>(stream, JsonOptions);
            if (recommendations?.Models is null)
                return new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            return recommendations.Models
                .Where(model => !string.IsNullOrWhiteSpace(model.ProviderKey) && !string.IsNullOrWhiteSpace(model.ModelId))
                .Select(model => ModelKey(model.ProviderKey, model.ModelId))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
        }
        catch (JsonException)
        {
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private static string ModelKey(string providerKey, string modelId)
        => $"{providerKey.Trim()}:{modelId.Trim()}";

    private static HpdosModelCatalogItem ToCatalogItem(
        string hpdosProviderKey,
        HpdosModelsDevModelSnapshot model)
        => new(
            ProviderKey: hpdosProviderKey,
            ModelId: model.ModelId,
            DisplayName: model.DisplayName,
            Family: model.Family,
            ReleaseDate: model.ReleaseDate,
            Status: NormalizeStatus(model.Status),
            Capabilities: model.Capabilities,
            Limits: model.Limits,
            Cost: model.Cost,
            ProviderOptionsSchema: HpdosProviderOptionSchema.ForProvider(hpdosProviderKey),
            Free: IsFree(model.Cost, model.ModelId, model.DisplayName),
            Recommended: false);

    private static HpdosModelCatalogItem ToCatalogItem(HpdosCustomModel model)
        => new(
            ProviderKey: model.ProviderKey,
            ModelId: model.ModelId,
            DisplayName: string.IsNullOrWhiteSpace(model.DisplayName) ? model.ModelId : model.DisplayName,
            Family: model.Family,
            ReleaseDate: null,
            Status: "active",
            Capabilities: new HpdosModelCapability(
                Tools: model.Tools,
                Reasoning: model.Reasoning,
                Vision: model.Vision,
                Audio: model.Audio,
                Attachments: model.Attachments,
                Local: model.Local),
            Limits: null,
            Cost: null,
            ProviderOptionsSchema: HpdosProviderOptionSchema.ForProvider(model.ProviderKey),
            Free: model.Free,
            Recommended: false);

    private static string NormalizeStatus(string? status)
        => string.Equals(status, "alpha", StringComparison.OrdinalIgnoreCase)
            || string.Equals(status, "beta", StringComparison.OrdinalIgnoreCase)
            || string.Equals(status, "deprecated", StringComparison.OrdinalIgnoreCase)
                ? status.ToLowerInvariant()
                : "active";

    private static bool IsFree(HpdosModelCost? cost, string modelId, string displayName)
    {
        if (modelId.Contains(":free", StringComparison.OrdinalIgnoreCase)
            || displayName.Contains("(free)", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (cost is null)
            return false;

        var hasPricedUnit = cost.Input.HasValue || cost.Output.HasValue || cost.CacheRead.HasValue || cost.CacheWrite.HasValue;
        return hasPricedUnit
            && (cost.Input ?? 0m) == 0m
            && (cost.Output ?? 0m) == 0m
            && (cost.CacheRead ?? 0m) == 0m
            && (cost.CacheWrite ?? 0m) == 0m;
    }
}

internal sealed class HpdosCustomModelStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly string storePath;
    private readonly SemaphoreSlim gate = new(1, 1);

    public HpdosCustomModelStore(string dataRoot)
    {
        storePath = Path.Combine(dataRoot, "provider-models.json");
    }

    public async Task<List<HpdosCustomModel>> ListAsync(CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try
        {
            var store = await ReadUnsafeAsync(ct);
            return store.Models
                .Select(Normalize)
                .Where(model => !string.IsNullOrWhiteSpace(model.ProviderKey) && !string.IsNullOrWhiteSpace(model.ModelId))
                .OrderBy(model => model.ProviderKey, StringComparer.OrdinalIgnoreCase)
                .ThenBy(model => model.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<HpdosCustomModel> UpsertAsync(HpdosCustomModel request, CancellationToken ct)
    {
        var model = Normalize(request);
        if (string.IsNullOrWhiteSpace(model.ProviderKey))
            throw new ArgumentException("Provider key is required.", nameof(request));
        if (string.IsNullOrWhiteSpace(model.ModelId))
            throw new ArgumentException("Model id is required.", nameof(request));

        await gate.WaitAsync(ct);
        try
        {
            var store = await ReadUnsafeAsync(ct);
            var nextModels = store.Models
                .Where(existing => !SameModel(existing, model))
                .Append(model)
                .OrderBy(item => item.ProviderKey, StringComparer.OrdinalIgnoreCase)
                .ThenBy(item => item.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ToList();

            await WriteUnsafeAsync(new HpdosCustomModelStoreFile(1, nextModels), ct);
            return model;
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<bool> DeleteAsync(string providerKey, string modelId, CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try
        {
            var store = await ReadUnsafeAsync(ct);
            var nextModels = store.Models
                .Where(model => !string.Equals(model.ProviderKey, providerKey, StringComparison.OrdinalIgnoreCase)
                    || !string.Equals(model.ModelId, modelId, StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (nextModels.Count == store.Models.Count)
                return false;

            await WriteUnsafeAsync(new HpdosCustomModelStoreFile(1, nextModels), ct);
            return true;
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task<HpdosCustomModelStoreFile> ReadUnsafeAsync(CancellationToken ct)
    {
        if (!File.Exists(storePath))
            return new HpdosCustomModelStoreFile(1, []);

        await using var stream = File.OpenRead(storePath);
        return await JsonSerializer.DeserializeAsync<HpdosCustomModelStoreFile>(stream, JsonOptions, ct)
            ?? new HpdosCustomModelStoreFile(1, []);
    }

    private async Task WriteUnsafeAsync(HpdosCustomModelStoreFile store, CancellationToken ct)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(storePath)!);
        await using var stream = File.Create(storePath);
        await JsonSerializer.SerializeAsync(stream, store, JsonOptions, ct);
    }

    private static HpdosCustomModel Normalize(HpdosCustomModel model)
        => model with
        {
            ProviderKey = model.ProviderKey.Trim(),
            ModelId = model.ModelId.Trim(),
            DisplayName = string.IsNullOrWhiteSpace(model.DisplayName) ? model.ModelId.Trim() : model.DisplayName.Trim(),
            Family = string.IsNullOrWhiteSpace(model.Family) ? null : model.Family.Trim()
        };

    private static bool SameModel(HpdosCustomModel left, HpdosCustomModel right)
        => string.Equals(left.ProviderKey, right.ProviderKey, StringComparison.OrdinalIgnoreCase)
            && string.Equals(left.ModelId, right.ModelId, StringComparison.OrdinalIgnoreCase);
}

internal sealed record HpdosModelsDevSnapshot(
    int Version,
    string Source,
    IReadOnlyList<HpdosModelsDevProviderSnapshot> Providers);

internal sealed record HpdosModelsDevProviderSnapshot(
    string ProviderId,
    string DisplayName,
    string? DocumentationUrl,
    IReadOnlyList<HpdosModelsDevModelSnapshot> Models);

internal sealed record HpdosModelsDevModelSnapshot(
    string ModelId,
    string DisplayName,
    string? Family,
    string? ReleaseDate,
    string? Status,
    HpdosModelCapability Capabilities,
    HpdosModelLimits? Limits,
    HpdosModelCost? Cost);

internal sealed record HpdosCustomModelStoreFile(
    int Version,
    List<HpdosCustomModel> Models);

internal sealed record HpdosRecommendedModelStoreFile(
    int Version,
    IReadOnlyList<HpdosRecommendedModel> Models);

internal sealed record HpdosRecommendedModel(
    string ProviderKey,
    string ModelId);

internal sealed record HpdosCustomModel(
    string ProviderKey,
    string ModelId,
    string DisplayName,
    string? Family = null,
    bool Tools = true,
    bool Reasoning = false,
    bool Vision = false,
    bool Audio = false,
    bool Attachments = true,
    bool Local = false,
    bool Free = false);

internal sealed record HpdosCustomModelRequest(
    string DisplayName,
    string? Family = null,
    bool Tools = true,
    bool Reasoning = false,
    bool Vision = false,
    bool Audio = false,
    bool Attachments = true,
    bool Local = false,
    bool Free = false);
