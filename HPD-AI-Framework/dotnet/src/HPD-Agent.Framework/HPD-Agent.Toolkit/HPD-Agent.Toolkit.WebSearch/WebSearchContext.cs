using System;
using System.Collections.Generic;
using System.Linq;
using HPD.Agent;

namespace HPD.Agent.Toolkit.WebSearch;

/// <summary>
/// The complete and corrected context for the WebSearch Toolkit.
/// </summary>
public class WebSearchContext : IToolMetadata
{
    private readonly IReadOnlyDictionary<string, IWebSearchConnector> _connectors;

    // Correctly defined properties with private setters
    public bool HasTavilyProvider { get; private set; }
    public bool HasBraveProvider { get; private set; }
    public bool HasBingProvider { get; private set; }
    public string DefaultProvider { get; private set; } = string.Empty;
    public string ConfiguredProviders { get; private set; } = string.Empty;

    public WebSearchContext(IEnumerable<IWebSearchConnector> connectors, string? defaultProvider = null)
    {
        if (connectors == null) throw new ArgumentNullException(nameof(connectors));
        
        var connectorList = connectors.ToList();
        _connectors = connectorList.ToDictionary(c => c.ProviderName.ToLowerInvariant(), c => c);
        
        // Initialize all properties
        InitializeProperties(defaultProvider);
    }

    private void InitializeProperties(string? defaultProvider)
    {
        // Set the property values
        DefaultProvider = defaultProvider?.ToLowerInvariant() ?? _connectors.Keys.FirstOrDefault() ?? "none";
        HasTavilyProvider = _connectors.ContainsKey("tavily");
        HasBraveProvider = _connectors.ContainsKey("brave");
        HasBingProvider = _connectors.ContainsKey("bing");
        ConfiguredProviders = _connectors.Any() ? string.Join(", ", _connectors.Keys) : "none";
    }
    
    // --- Restored Helper Methods ---
    
    public bool HasProvider(string providerName)
    {
        return !string.IsNullOrEmpty(providerName) && _connectors.ContainsKey(providerName.ToLowerInvariant());
    }

    public IWebSearchConnector GetConnector(string providerName)
    {
        if (string.IsNullOrEmpty(providerName)) throw new ArgumentException("Provider name cannot be null or empty", nameof(providerName));
        if (!_connectors.TryGetValue(providerName.ToLowerInvariant(), out var connector))
            throw new InvalidOperationException($"Provider '{providerName}' is not configured");
        return connector;
    }

    public IWebSearchConnector GetDefaultConnector()
    {
        if (string.IsNullOrEmpty(DefaultProvider) || DefaultProvider == "none")
        {
            throw new InvalidOperationException("A default provider is not configured.");
        }
        return GetConnector(DefaultProvider);
    }

    #region IToolMetadata Implementation
    public T? GetProperty<T>(string propertyName, T? defaultValue = default)
    {
        object? value = propertyName.ToLowerInvariant() switch
        {
            "hastavilyprovider" => HasTavilyProvider,
            "hasbraveprovider" => HasBraveProvider,
            "hasbingprovider" => HasBingProvider,
            "defaultprovider" => DefaultProvider,
            "configuredproviders" => ConfiguredProviders,
            _ => null
        };
        if (value is T typedValue) return typedValue;
        return defaultValue;
    }

    public bool HasProperty(string propertyName) => propertyName.ToLowerInvariant() switch
    {
        "hastavilyprovider" or "hasbraveprovider" or "hasbingprovider" or "defaultprovider" or "configuredproviders" => true,
        _ => false
    };

    public IEnumerable<string> GetPropertyNames() => new[] { "HasTavilyProvider", "HasBraveProvider", "HasBingProvider", "DefaultProvider", "ConfiguredProviders" };
    #endregion
}