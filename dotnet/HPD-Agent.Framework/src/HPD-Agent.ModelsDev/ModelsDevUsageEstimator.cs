using HPD.Agent.Providers;
using Microsoft.Extensions.AI;

namespace HPD.Agent.ModelsDev;

public sealed record ModelsDevCatalogProvenance(
    DateTimeOffset RetrievedAt, string? ETag, string ContentDigest, Uri Source, ModelsDevCatalogOrigin Origin);

public sealed record ModelsDevModelResolution(
    string ProviderId, string ObservedModelId, string CatalogModelId, string ResolutionRule);

public sealed record ModelsDevUsageDiagnostic(string Code, string Message, bool IsError);

public sealed record ModelsDevNormalizedUsage(
    long? StandardInput,
    long? CachedInput,
    long? CacheWriteInput,
    long? AudioInput,
    long? StandardOutput,
    long? ReasoningOutput,
    long? AudioOutput,
    long? ContextInput,
    IReadOnlyDictionary<string, long> UnmappedCounts,
    IReadOnlyList<ModelsDevUsageDiagnostic> Diagnostics);

public interface IModelsDevUsageNormalizationContributor
{
    bool AppliesTo(string providerKey);
    void Contribute(UsageDetails usage, ModelsDevUsageNormalizationBuilder builder);
}

public sealed class ModelsDevUsageNormalizationBuilder
{
    private readonly Dictionary<string, long> _counts;
    private readonly List<ModelsDevUsageDiagnostic> _diagnostics = [];

    internal ModelsDevUsageNormalizationBuilder(UsageDetails usage)
    {
        _counts = usage.AdditionalCounts is null
            ? new(StringComparer.Ordinal)
            : new(usage.AdditionalCounts, StringComparer.Ordinal);
    }

    public IReadOnlyDictionary<string, long> UnmappedCounts => _counts;
    public IReadOnlyList<ModelsDevUsageDiagnostic> Diagnostics => _diagnostics;

    public long? Consume(string key)
    {
        if (!_counts.Remove(key, out var value)) return null;
        return value;
    }

    public void AddDiagnostic(string code, string message, bool isError = false)
        => _diagnostics.Add(new(code, message, isError));
}

public interface IModelsDevUsageEstimator : IProviderUsageValuationAuthority;

public sealed record ModelsDevValuationProvenance(
    ModelsDevCatalogProvenance Catalog,
    ModelsDevModelResolution? Model,
    string AlgorithmRevision) : ProviderUsageValuationProvenance
{
    public override ProviderUsageValuationAuthorityKind? AuthorityKind => ProviderUsageValuationAuthorityKind.CatalogEstimate;
}

public sealed record ModelsDevValuationDetails(
    ModelsDevNormalizedUsage NormalizedUsage,
    ModelsDevModelResolution? Model) : ProviderUsageValuationDetails;

public sealed record ModelsDevRateSelection(
    string RateSet,
    long? ContextThreshold,
    bool UsedFallback,
    string? FallbackReason) : ProviderValuationComponentProvenance;

public sealed class ModelsDevUsageEstimator : IModelsDevUsageEstimator
{
    public const string CurrentAlgorithmRevision = "models.dev-v1";
    private const decimal TokenRateDenominator = 1_000_000m;
    private readonly IModelsDevCatalog _catalog;
    private readonly ModelsDevProviderMappings _mappings;
    private readonly IReadOnlyList<IModelsDevUsageNormalizationContributor> _contributors;
    private readonly bool _allowReasoningOutputRateFallback;

    public ModelsDevUsageEstimator(
        IModelsDevCatalog catalog,
        ModelsDevProviderMappings? mappings = null,
        IEnumerable<IModelsDevUsageNormalizationContributor>? contributors = null,
        bool allowReasoningOutputRateFallback = false)
    {
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _mappings = mappings ?? ModelsDevProviderMappings.Default;
        _contributors = contributors?.ToArray() ?? [];
        _allowReasoningOutputRateFallback = allowReasoningOutputRateFallback;
    }

    public string AuthorityId => "models.dev";

    public async ValueTask<ProviderUsageValuation> ValueAsync(
        ProviderUsageValuationInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var measurement = input.Measurement;
        if (measurement.Usage is null)
            return Unavailable(measurement, "usage_missing", "The provider operation reported no usage.");
        if (string.IsNullOrWhiteSpace(measurement.ProviderKey))
            return Unavailable(measurement, "provider_missing", "The provider identity is missing.");
        if (string.IsNullOrWhiteSpace(measurement.ModelId))
            return Unavailable(measurement, "model_missing", "The model identity is missing.");
        var providerId = _mappings.ToModelsDevProviderId(measurement.ProviderKey);
        if (providerId is null)
            return Unavailable(measurement, "provider_unmapped", "The provider has no models.dev mapping.");

        var snapshot = await _catalog.GetSnapshotAsync(ModelsDevRefreshMode.IfStale, cancellationToken).ConfigureAwait(false);
        if (!snapshot.Database.Providers.TryGetValue(providerId, out var provider))
            return Unavailable(measurement, "provider_unresolved", "The mapped provider is absent from the catalog.");
        if (!TryResolveModel(providerId, provider, measurement.ModelId, out var modelId, out var model, out var rule))
            return Unavailable(measurement, "model_unresolved", "The observed model has no exact or registered canonical catalog match.");
        if (model.Cost is null)
            return Unavailable(measurement, "cost_missing", "The resolved catalog model has no cost object.");

        var resolution = new ModelsDevModelResolution(providerId, measurement.ModelId, modelId, rule);
        var normalized = Normalize(measurement.ProviderKey, measurement.Usage);
        if (normalized.Diagnostics.Any(static diagnostic => diagnostic.IsError))
            return Invalid(measurement, normalized.Diagnostics);

        var tier = SelectRates(model.Cost, normalized.ContextInput);
        var components = new List<ProviderUsageValuationComponent>();
        var unpriced = new List<ProviderUsageUnpricedQuantity>();
        var diagnostics = normalized.Diagnostics.Select(static diagnostic => new ProviderUsageValuationDiagnostic(
            diagnostic.Code, diagnostic.Message,
            diagnostic.IsError ? ProviderUsageValuationDiagnosticSeverity.Error : ProviderUsageValuationDiagnosticSeverity.Warning)).ToList();
        var missingInputUncertainty = !measurement.Usage.InputTokenCount.HasValue;
        if (missingInputUncertainty)
        {
            diagnostics.Add(new("input_count_missing",
                "Input usage was not reported; independently priceable output is only a partial estimate.",
                ProviderUsageValuationDiagnosticSeverity.Warning));
        }

        Price("input", normalized.StandardInput, tier?.Input, tier, components, unpriced);
        Price("cache_read", normalized.CachedInput, tier?.CacheRead, tier, components, unpriced);
        Price("cache_write", normalized.CacheWriteInput, tier?.CacheWrite, tier, components, unpriced);
        Price("input_audio", normalized.AudioInput, tier?.InputAudio, tier, components, unpriced);
        Price("output", normalized.StandardOutput, tier?.Output, tier, components, unpriced);
        var reasoningRate = tier?.Reasoning;
        var reasoningFallback = false;
        if (normalized.ReasoningOutput.HasValue && reasoningRate is null && _allowReasoningOutputRateFallback)
        {
            reasoningRate = tier?.Output;
            reasoningFallback = reasoningRate.HasValue;
        }
        Price("reasoning", normalized.ReasoningOutput, reasoningRate, tier, components, unpriced,
            reasoningFallback, reasoningFallback ? "reasoning-rate-missing" : null);
        Price("output_audio", normalized.AudioOutput, tier?.OutputAudio, tier, components, unpriced);
        foreach (var count in normalized.UnmappedCounts)
            unpriced.Add(new(count.Key, count.Value, "count", "No registered models.dev pricing dimension."));

        if (components.Count == 0)
            return Unavailable(measurement, "no_priceable_usage", "No reported quantity has a determinable models.dev rate.");
        var amount = components.Sum(static component => component.Amount);
        var status = missingInputUncertainty || unpriced.Count > 0
            ? ProviderUsageValuationStatus.Partial
            : ProviderUsageValuationStatus.Complete;
        return new(measurement.SourceEventId, AuthorityId, ProviderUsageValuationAuthorityKind.CatalogEstimate,
            status, amount, "USD", components, unpriced,
            new ModelsDevValuationProvenance(new(snapshot.RetrievedAt, snapshot.ETag, snapshot.ContentDigest, snapshot.Source, snapshot.Origin),
                resolution, CurrentAlgorithmRevision),
            new ModelsDevValuationDetails(normalized, resolution), diagnostics);
    }

    private ModelsDevNormalizedUsage Normalize(string providerKey, UsageDetails usage)
    {
        var builder = new ModelsDevUsageNormalizationBuilder(usage);
        foreach (var contributor in _contributors)
            if (contributor.AppliesTo(providerKey)) contributor.Contribute(usage, builder);
        var cacheWrite = builder.Consume(AgentUsageCountKeys.CacheWriteInputTokens);
        var cacheWrite5Minute = builder.Consume(AgentUsageCountKeys.CacheWriteInputTokens5Minute);
        var cacheWrite1Hour = builder.Consume(AgentUsageCountKeys.CacheWriteInputTokens1Hour);
        var cacheWriteParts = cacheWrite5Minute.GetValueOrDefault() + cacheWrite1Hour.GetValueOrDefault();
        if (cacheWrite.HasValue && (cacheWrite5Minute.HasValue || cacheWrite1Hour.HasValue) && cacheWrite.Value != cacheWriteParts)
            builder.AddDiagnostic("cache_write_counts_conflict", "Aggregate and duration-specific cache-write counts disagree.", true);
        cacheWrite ??= cacheWrite5Minute.HasValue || cacheWrite1Hour.HasValue ? cacheWriteParts : null;
        var input = usage.InputTokenCount;
        var cached = usage.CachedInputTokenCount;
#pragma warning disable MEAI001
        var audioInput = usage.InputAudioTokenCount;
        var output = usage.OutputTokenCount;
        var reasoning = usage.ReasoningTokenCount;
        var audioOutput = usage.OutputAudioTokenCount;
#pragma warning restore MEAI001
        var diagnostics = builder.Diagnostics.ToList();

        static long? Subtract(long? total, params long?[] portions)
            => total.HasValue ? total.Value - portions.Where(static value => value.HasValue).Sum(static value => value!.Value) : null;
        var standardInput = Subtract(input, cached, cacheWrite, audioInput);
        var standardOutput = Subtract(output, reasoning, audioOutput);
        if (new[] { standardInput, standardOutput, cached, cacheWrite, audioInput, reasoning, audioOutput }
            .Any(static value => value < 0))
            diagnostics.Add(new("negative_exclusive_bucket", "A reported subdivision exceeds its inclusive parent total.", true));
        if (cached > 0 && audioInput > 0)
            diagnostics.Add(new("cached_audio_overlap_unknown", "Cached and audio input overlap is not reported by the provider.", true));
        if (cacheWrite > 0 && audioInput > 0)
            diagnostics.Add(new("cache_write_audio_overlap_unknown", "Cache-write and audio input overlap is not reported by the provider.", true));
        if (reasoning > 0 && audioOutput > 0)
            diagnostics.Add(new("reasoning_audio_overlap_unknown", "Reasoning and audio output overlap is not reported by the provider.", true));
        return new(standardInput, cached, cacheWrite, audioInput, standardOutput, reasoning, audioOutput,
            input, new Dictionary<string, long>(builder.UnmappedCounts), diagnostics);
    }

    private static bool TryResolveModel(string providerId, ModelsDevProvider provider, string observed,
        out string selected, out ModelsDevModel model, out string rule)
    {
        selected = observed;
        rule = "exact";
        if (provider.Models.TryGetValue(observed, out model!)) return true;
        if (string.Equals(providerId, "amazon-bedrock", StringComparison.OrdinalIgnoreCase)
            && ModelsDevStore.TryStripBedrockPrefix(observed, out selected)
            && provider.Models.TryGetValue(selected, out model!))
        {
            rule = "bedrock-region-prefix";
            return true;
        }
        model = null!;
        return false;
    }

    private static ModelsDevRateSet? SelectRates(ModelsDevCost cost, long? contextInput)
    {
        if (cost.Tiers.Count > 0 && !contextInput.HasValue) return null;
        var tier = cost.Tiers.Where(item => contextInput > item.Tier.Size)
            .OrderByDescending(static item => item.Tier.Size).FirstOrDefault();
        return tier is null
            ? new("base", null, cost.Input, cost.Output, cost.Reasoning, cost.CacheRead, cost.CacheWrite, cost.InputAudio, cost.OutputAudio)
            : new("tier", tier.Tier.Size, tier.Input, tier.Output, tier.Reasoning, tier.CacheRead, tier.CacheWrite, tier.InputAudio, tier.OutputAudio);
    }

    private static void Price(string category, long? quantity, decimal? rate, ModelsDevRateSet? rateSet,
        List<ProviderUsageValuationComponent> components, List<ProviderUsageUnpricedQuantity> unpriced,
        bool fallback = false, string? fallbackReason = null)
    {
        if (!quantity.HasValue) return;
        if (!rate.HasValue || rateSet is null)
        {
            unpriced.Add(new(category, quantity.Value, "token", rateSet is null ? "Context tier cannot be selected." : "Catalog rate is missing."));
            return;
        }
        var amount = quantity.Value * rate.Value / TokenRateDenominator;
        components.Add(new(category, quantity.Value, "token", rate, "USD", TokenRateDenominator, "token", amount,
            new ModelsDevRateSelection(rateSet.Name, rateSet.ContextThreshold, fallback, fallbackReason)));
    }

    private ProviderUsageValuation Unavailable(ProviderUsageMeasurement measurement, string code, string message)
        => ProviderUsageValuation.Unavailable(measurement.SourceEventId, AuthorityId,
            ProviderUsageValuationAuthorityKind.CatalogEstimate, CurrentAlgorithmRevision, code, message);

    private ProviderUsageValuation Invalid(ProviderUsageMeasurement measurement, IReadOnlyList<ModelsDevUsageDiagnostic> source)
        => new(measurement.SourceEventId, AuthorityId, ProviderUsageValuationAuthorityKind.CatalogEstimate,
            ProviderUsageValuationStatus.InvalidUsage, null, null, [], [],
            new AuthorityAttemptValuationProvenance(AuthorityId, CurrentAlgorithmRevision, "invalid_usage"), null,
            source.Select(static diagnostic => new ProviderUsageValuationDiagnostic(diagnostic.Code, diagnostic.Message,
                ProviderUsageValuationDiagnosticSeverity.Error)).ToArray());

    private sealed record ModelsDevRateSet(string Name, long? ContextThreshold, decimal Input, decimal Output,
        decimal? Reasoning, decimal? CacheRead, decimal? CacheWrite, decimal? InputAudio, decimal? OutputAudio);
}
