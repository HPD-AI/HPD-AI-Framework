using System.Collections.ObjectModel;
using System.Text.Json;

namespace HPD.Agent.Providers;

/// <summary>Identifies the layer that supplied an effective configuration value.</summary>
public enum ProviderConfigurationSource
{
    /// <summary>The provider descriptor's final host fallback.</summary>
    HostFallback = 0,

    /// <summary>A matching provider profile.</summary>
    ProviderProfile = 1,

    /// <summary>The agent's selected family default.</summary>
    AgentDefault = 2,

    /// <summary>The explicit invocation override.</summary>
    RunOverride = 3,

    /// <summary>An explicit borrowed runtime client or component factory.</summary>
    RuntimeOverride = 4
}

/// <summary>Contains an immutable provider-family selection and field provenance.</summary>
public sealed class ResolvedProviderFamilyPlan
{
    internal ResolvedProviderFamilyPlan(
        ProviderClientFamily family,
        string providerKey,
        string modelName,
        string? endpoint,
        string? authenticationKey,
        IReadOnlyDictionary<string, string>? customHeaders,
        JsonElement? constructionOptions,
        IReadOnlyDictionary<string, ProviderConfigurationSource> provenance)
    {
        Family = family;
        ProviderKey = providerKey;
        ModelName = modelName;
        Endpoint = endpoint;
        AuthenticationKey = authenticationKey;
        CustomHeaders = customHeaders;
        ConstructionOptions = constructionOptions;
        Provenance = provenance;
    }

    /// <summary>Gets the active client family.</summary>
    public ProviderClientFamily Family { get; }
    /// <summary>Gets the canonical provider key.</summary>
    public string ProviderKey { get; }
    /// <summary>Gets the effective model name.</summary>
    public string ModelName { get; }
    /// <summary>Gets the effective endpoint.</summary>
    public string? Endpoint { get; }
    /// <summary>Gets the selected named authentication registration.</summary>
    public string? AuthenticationKey { get; }
    /// <summary>Gets copied non-authentication request headers.</summary>
    public IReadOnlyDictionary<string, string>? CustomHeaders { get; }
    /// <summary>Gets a cloned provider-specific construction payload.</summary>
    public JsonElement? ConstructionOptions { get; }
    /// <summary>Gets the source of every effective field.</summary>
    public IReadOnlyDictionary<string, ProviderConfigurationSource> Provenance { get; }
}

/// <summary>Resolves provider-bound family layers without mutating any input.</summary>
public static class ProviderFamilyPlanResolver
{
    /// <summary>Resolves a family using host, profile, agent, then run precedence.</summary>
    public static ResolvedProviderFamilyPlan Resolve(
        ProviderClientFamily family,
        IProviderDescriptorRegistry descriptors,
        ProviderClientConfig? hostFallback,
        ProviderClientConfig? providerProfile,
        ProviderClientConfig? agentDefault,
        ProviderClientConfig? runOverride)
    {
        ArgumentNullException.ThrowIfNull(descriptors);
        var selectedKey = FirstKey(runOverride, agentDefault, providerProfile, hostFallback)
            ?? throw new AgentRunConfigurationException(
                "ProviderKeyRequired",
                $"Clients.{family}.ProviderKey",
                $"No provider is configured for active client family '{family}'.");
        var canonical = descriptors.Canonicalize(selectedKey);
        var layers = new[]
        {
            (hostFallback, ProviderConfigurationSource.HostFallback),
            (providerProfile, ProviderConfigurationSource.ProviderProfile),
            (agentDefault, ProviderConfigurationSource.AgentDefault),
            (runOverride, ProviderConfigurationSource.RunOverride)
        };

        ProviderClientConfig? effective = null;
        var provenance = new Dictionary<string, ProviderConfigurationSource>(StringComparer.Ordinal);
        foreach (var (layer, source) in layers)
        {
            if (layer is null)
                continue;
            var layerKey = string.IsNullOrWhiteSpace(layer.ProviderKey)
                ? canonical
                : descriptors.Canonicalize(layer.ProviderKey);
            if (!string.Equals(canonical, layerKey, StringComparison.Ordinal))
                continue;
            effective ??= new ProviderClientConfig();
            Apply(effective, layer, source, provenance);
            if (canonical is not null)
                effective.ProviderKey = canonical;
        }

        if (effective is null)
            throw new InvalidOperationException("The selected provider did not produce an effective family configuration.");
        if (string.IsNullOrWhiteSpace(effective.ModelName))
            throw new AgentRunConfigurationException(
                "ModelNameRequired",
                $"Clients.{family}.ModelName",
                $"No model is configured for provider '{canonical}' and client family '{family}'.",
                canonical);

        return new ResolvedProviderFamilyPlan(
            family,
            canonical,
            effective.ModelName,
            effective.Endpoint,
            effective.AuthenticationKey,
            effective.CustomHeaders is null
                ? null
                : new ReadOnlyDictionary<string, string>(new Dictionary<string, string>(effective.CustomHeaders, StringComparer.OrdinalIgnoreCase)),
            effective.ConstructionOptions?.Clone(),
            new ReadOnlyDictionary<string, ProviderConfigurationSource>(provenance));
    }

    private static string? FirstKey(params ProviderClientConfig?[] layers) =>
        layers.Select(static layer => layer?.ProviderKey)
            .FirstOrDefault(static key => !string.IsNullOrWhiteSpace(key));

    private static void Apply(
        ProviderClientConfig target,
        ProviderClientConfig source,
        ProviderConfigurationSource layer,
        Dictionary<string, ProviderConfigurationSource> provenance)
    {
        SetString(nameof(ProviderClientConfig.ProviderKey), source.ProviderKey, value => target.ProviderKey = value);
        SetString(nameof(ProviderClientConfig.ModelName), source.ModelName, value => target.ModelName = value);
        SetString(nameof(ProviderClientConfig.Endpoint), source.Endpoint, value => target.Endpoint = value);
        SetString(nameof(ProviderClientConfig.AuthenticationKey), source.AuthenticationKey, value => target.AuthenticationKey = value);
        if (source.CustomHeaders is not null)
        {
            target.CustomHeaders = new Dictionary<string, string>(source.CustomHeaders, StringComparer.OrdinalIgnoreCase);
            provenance[nameof(ProviderClientConfig.CustomHeaders)] = layer;
        }
        if (source.ConstructionOptions is not null)
        {
            target.ConstructionOptions = source.ConstructionOptions.Value.Clone();
            provenance[nameof(ProviderClientConfig.ConstructionOptions)] = layer;
        }
        return;

        void SetString(string name, string? value, Action<string> setter)
        {
            if (string.IsNullOrWhiteSpace(value))
                return;
            setter(value);
            provenance[name] = layer;
        }
    }
}
