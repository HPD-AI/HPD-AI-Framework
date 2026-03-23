using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Configuration;
using HPD.Agent;

namespace HPD.Agent.Toolkit.WebSearch;

/// <summary>
/// Extension methods for AgentBuilder to configure web search providers with type-safe fluent builders
/// </summary>
public static class AgentBuilderWebSearchExtensions
{
    // Store connectors and default provider preferences
    private static readonly Dictionary<AgentBuilder, List<IWebSearchConnector>> _pendingConnectors = new();
    private static readonly Dictionary<AgentBuilder, List<Func<IConfiguration?, IWebSearchConnector>>> _pendingBuilders = new();
    private static readonly Dictionary<AgentBuilder, string?> _defaultProviders = new();
    
    /// <summary>
    /// Configures Tavily web search provider with fluent builder pattern
    /// </summary>
    /// <param name="builder">The agent builder</param>
    /// <param name="configure">Configuration action for Tavily settings</param>
    /// <returns>The agent builder for chaining</returns>
    public static AgentBuilder WithTavilyWebSearch(this AgentBuilder builder,
        Func<ITavilyWebSearchBuilder, ITavilyWebSearchBuilder> configure)
    {
        if (builder == null) throw new ArgumentNullException(nameof(builder));
        if (configure == null) throw new ArgumentNullException(nameof(configure));

        var tavilyBuilder = new TavilyWebSearchBuilder(builder.Configuration);
        var configuredBuilder = configure(tavilyBuilder);
        var connector = ((IWebSearchProviderBuilder<ITavilyWebSearchBuilder>)configuredBuilder).Build();

        AddWebSearchConnector(builder, connector);

        // Auto-finalize to register the Toolkit immediately
        return FinalizeWebSearch(builder);
    }

    /// <summary>
    /// Configures Tavily web search provider with default settings.
    /// The API key will be automatically resolved from configuration when the agent is built.
    /// </summary>
    /// <param name="builder">The agent builder</param>
    /// <returns>The agent builder for chaining</returns>
    public static AgentBuilder WithTavilyWebSearch(this AgentBuilder builder)
    {
        if (builder == null) throw new ArgumentNullException(nameof(builder));

        // Defer building until FinalizeWebSearch() when configuration is available
        AddWebSearchBuilder(builder, (config) =>
        {
            var tavilyBuilder = new TavilyWebSearchBuilder(config);
            return ((IWebSearchProviderBuilder<ITavilyWebSearchBuilder>)tavilyBuilder).Build();
        });

        // Auto-finalize to register the Toolkit immediately
        return FinalizeWebSearch(builder);
    }
    
    /// <summary>
    /// Configures Brave web search provider with fluent builder pattern
    /// </summary>
    /// <param name="builder">The agent builder</param>
    /// <param name="configure">Configuration action for Brave settings</param>
    /// <returns>The agent builder for chaining</returns>
    public static AgentBuilder WithBraveWebSearch(this AgentBuilder builder,
        Func<IBraveWebSearchBuilder, IBraveWebSearchBuilder> configure)
    {
        if (builder == null) throw new ArgumentNullException(nameof(builder));
        if (configure == null) throw new ArgumentNullException(nameof(configure));
        
        var braveBuilder = new BraveWebSearchBuilder(builder.Configuration);
        var configuredBuilder = configure(braveBuilder);
        var connector = ((IWebSearchProviderBuilder<IBraveWebSearchBuilder>)configuredBuilder).Build();
        
        AddWebSearchConnector(builder, connector);
        
        // Auto-finalize to register the Toolkit immediately
        return FinalizeWebSearch(builder);
    }

    /// <summary>
    /// Configures Brave web search provider with default settings
    /// </summary>
    /// <param name="builder">The agent builder</param>
    /// <returns>The agent builder for chaining</returns>
    public static AgentBuilder WithBraveWebSearch(this AgentBuilder builder)
    {
        if (builder == null) throw new ArgumentNullException(nameof(builder));
        
        var braveBuilder = new BraveWebSearchBuilder(builder.Configuration);
        var connector = ((IWebSearchProviderBuilder<IBraveWebSearchBuilder>)braveBuilder).Build();
        
        AddWebSearchConnector(builder, connector);
        
        // Auto-finalize to register the Toolkit immediately
        return FinalizeWebSearch(builder);
    }
    
    /// <summary>
    /// Configures Bing web search provider with fluent builder pattern
    /// </summary>
    /// <param name="builder">The agent builder</param>
    /// <param name="configure">Configuration action for Bing settings</param>
    /// <returns>The agent builder for chaining</returns>
    public static AgentBuilder WithBingWebSearch(this AgentBuilder builder,
        Func<IBingWebSearchBuilder, IBingWebSearchBuilder> configure)
    {
        if (builder == null) throw new ArgumentNullException(nameof(builder));
        if (configure == null) throw new ArgumentNullException(nameof(configure));
        
        var bingBuilder = new BingWebSearchBuilder(builder.Configuration);
        var configuredBuilder = configure(bingBuilder);
        var connector = ((IWebSearchProviderBuilder<IBingWebSearchBuilder>)configuredBuilder).Build();
        
        AddWebSearchConnector(builder, connector);
        
        // Auto-finalize to register the Toolkit immediately
        return FinalizeWebSearch(builder);
    }

    /// <summary>
    /// Configures Bing web search provider with default settings
    /// </summary>
    /// <param name="builder">The agent builder</param>
    /// <returns>The agent builder for chaining</returns>
    public static AgentBuilder WithBingWebSearch(this AgentBuilder builder)
    {
        if (builder == null) throw new ArgumentNullException(nameof(builder));
        
        var bingBuilder = new BingWebSearchBuilder(builder.Configuration);
        var connector = ((IWebSearchProviderBuilder<IBingWebSearchBuilder>)bingBuilder).Build();
        
        AddWebSearchConnector(builder, connector);
        
        // Auto-finalize to register the Toolkit immediately
        return FinalizeWebSearch(builder);
    }
    
    /// <summary>
    /// Adds a web search connector to the agent's capabilities
    /// The WebSearchTools will be automatically created when all providers are configured
    /// </summary>
    /// <param name="builder">The agent builder</param>
    /// <param name="connector">The configured web search connector</param>
    /// <returns>The agent builder for chaining</returns>
    private static AgentBuilder AddWebSearchConnector(AgentBuilder builder, IWebSearchConnector connector)
    {
        // Store the connector for later collection
        if (!_pendingConnectors.ContainsKey(builder))
        {
            _pendingConnectors[builder] = new List<IWebSearchConnector>();
        }

        _pendingConnectors[builder].Add(connector);
        return builder;
    }

    /// <summary>
    /// Adds a deferred web search builder that will be built when configuration is available
    /// </summary>
    /// <param name="builder">The agent builder</param>
    /// <param name="builderFunc">Function that creates the connector when given configuration</param>
    /// <returns>The agent builder for chaining</returns>
    private static AgentBuilder AddWebSearchBuilder(AgentBuilder builder, Func<IConfiguration?, IWebSearchConnector> builderFunc)
    {
        // Store the builder function for later execution
        if (!_pendingBuilders.ContainsKey(builder))
        {
            _pendingBuilders[builder] = new List<Func<IConfiguration?, IWebSearchConnector>>();
        }

        _pendingBuilders[builder].Add(builderFunc);
        return builder;
    }
    
    /// <summary>
    /// Sets the default web search provider to use when no provider is specified
    /// </summary>
    /// <param name="builder">The agent builder</param>
    /// <param name="providerName">Name of the provider to use as default (tavily, brave, bing)</param>
    /// <returns>The agent builder for chaining</returns>
    public static AgentBuilder WithDefaultWebSearchProvider(this AgentBuilder builder, string providerName)
    {
        if (builder == null) throw new ArgumentNullException(nameof(builder));
        if (string.IsNullOrWhiteSpace(providerName)) throw new ArgumentException("Provider name cannot be empty", nameof(providerName));
        
        // Store the default provider preference
        _defaultProviders[builder] = providerName.ToLowerInvariant();
        
        // Note: Toolkit will be created when FinalizeWebSearch() is called
        
        return builder;
    }

    /// <summary>
    /// Finalizes web search configuration and creates the WebSearchTools with all configured providers.
    /// This is called automatically by the With*WebSearch() extension methods.
    /// </summary>
    /// <param name="builder">The agent builder</param>
    /// <returns>The agent builder for chaining</returns>
    public static AgentBuilder FinalizeWebSearch(this AgentBuilder builder)
    {
        if (builder == null) throw new ArgumentNullException(nameof(builder));

        var connectors = new List<IWebSearchConnector>();

        // Build any deferred builders with the current configuration
        if (_pendingBuilders.TryGetValue(builder, out var builders))
        {
            foreach (var builderFunc in builders)
            {
                var connector = builderFunc(builder.Configuration);
                connectors.Add(connector);
            }
        }

        // Add any already-built connectors
        if (_pendingConnectors.TryGetValue(builder, out var existingConnectors))
        {
            connectors.AddRange(existingConnectors);
        }

        // If no connectors at all, return silently
        if (!connectors.Any())
        {
            return builder;
        }

        // Get the default provider preference (if set)
        _defaultProviders.TryGetValue(builder, out var defaultProvider);

        // Create the WebSearchContext with all connectors
        var context = new WebSearchContext(connectors, defaultProvider);

        // Register the WebSearchTools with the context
        var Toolkit = new WebSearchTools(context);
        builder.WithTools(Toolkit, context);

        // Clean up the temporary storage
        _pendingConnectors.Remove(builder);
        _pendingBuilders.Remove(builder);
        _defaultProviders.Remove(builder);

        return builder;
    }

    /// <summary>
    /// Cleanup method to remove temporary storage when agent is built.
    /// Called automatically by FinalizeWebSearch().
    /// </summary>
    public static void CleanupWebSearchStorage(AgentBuilder builder)
    {
        _pendingConnectors.Remove(builder);
        _pendingBuilders.Remove(builder);
        _defaultProviders.Remove(builder);
    }
}

/// <summary>
/// Placeholder implementations for other providers
/// These will be implemented in future iterations
/// </summary>
public class BraveWebSearchBuilder : IBraveWebSearchBuilder
{
    private readonly BraveConfig _config = new();
    private readonly IConfiguration? _configuration;

    public BraveWebSearchBuilder(IConfiguration? configuration = null)
    {
        _configuration = configuration;
    }
    
    public IBraveWebSearchBuilder WithApiKey(string apiKey)
    {
        _config.ApiKey = apiKey ?? throw new ArgumentNullException(nameof(apiKey));
        return this;
    }
    
    public IBraveWebSearchBuilder WithTimeout(TimeSpan timeout)
    {
        _config.Timeout = timeout;
        return this;
    }
    
    public IBraveWebSearchBuilder WithRetryPolicy(int retries, TimeSpan delay)
    {
        _config.RetryCount = retries;
        _config.RetryDelay = delay;
        return this;
    }
    
    public IBraveWebSearchBuilder OnError(Action<Exception> errorHandler)
    {
        _config.ErrorHandler = errorHandler;
        return this;
    }
    
    public IBraveWebSearchBuilder WithSafeSearch(BraveSafeSearch safeSearch)
    {
        _config.SafeSearch = safeSearch;
        return this;
    }
    
    public IBraveWebSearchBuilder WithCountry(string countryCode)
    {
        _config.Country = countryCode;
        return this;
    }
    
    public IBraveWebSearchBuilder WithSearchLanguage(string language)
    {
        _config.SearchLanguage = language;
        return this;
    }
    
    public IBraveWebSearchBuilder WithUILanguage(string uiLanguage)
    {
        _config.UILanguage = uiLanguage;
        return this;
    }
    
    public IBraveWebSearchBuilder WithResultFilter(string filter)
    {
        _config.ResultFilter = filter;
        return this;
    }
    
    public IBraveWebSearchBuilder WithUnits(BraveUnits units)
    {
        _config.Units = units;
        return this;
    }
    
    public IBraveWebSearchBuilder EnableSpellCheck(bool enable = true)
    {
        _config.SpellCheck = enable;
        return this;
    }
    
    public IBraveWebSearchBuilder EnableExtraSnippets(bool enable = true)
    {
        _config.ExtraSnippets = enable;
        return this;
    }
    
    
    
    IWebSearchConnector IWebSearchProviderBuilder<IBraveWebSearchBuilder>.Build()
    {
        // If no API key was provided manually, try to resolve it from configuration
        if (string.IsNullOrWhiteSpace(_config.ApiKey))
        {
            _config.ApiKey = _configuration?["Brave:ApiKey"] ?? string.Empty;
        }
        // Apply fixed defaults for Brave
        if (!_config.Timeout.HasValue) _config.Timeout = TimeSpan.FromSeconds(30);
        if (string.IsNullOrWhiteSpace(_config.Country)) _config.Country = "US";
        
        _config.Validate();
        // TODO: Implement BraveConnector
        throw new NotImplementedException("BraveConnector implementation coming in next iteration");
    }
}

public class BingWebSearchBuilder : IBingWebSearchBuilder
{
    private readonly BingConfig _config = new();
    private readonly IConfiguration? _configuration;

    public BingWebSearchBuilder(IConfiguration? configuration = null)
    {
        _configuration = configuration;
    }
    
    public IBingWebSearchBuilder WithApiKey(string apiKey)
    {
        _config.ApiKey = apiKey ?? throw new ArgumentNullException(nameof(apiKey));
        return this;
    }
    
    public IBingWebSearchBuilder WithTimeout(TimeSpan timeout)
    {
        _config.Timeout = timeout;
        return this;
    }
    
    public IBingWebSearchBuilder WithRetryPolicy(int retries, TimeSpan delay)
    {
        _config.RetryCount = retries;
        _config.RetryDelay = delay;
        return this;
    }
    
    public IBingWebSearchBuilder OnError(Action<Exception> errorHandler)
    {
        _config.ErrorHandler = errorHandler;
        return this;
    }
    
    public IBingWebSearchBuilder WithEndpoint(string endpoint)
    {
        _config.Endpoint = endpoint;
        return this;
    }
    
    public IBingWebSearchBuilder WithMarket(string market)
    {
        _config.Market = market;
        return this;
    }
    
    public IBingWebSearchBuilder WithSafeSearch(BingSafeSearch safeSearch)
    {
        _config.SafeSearch = safeSearch;
        return this;
    }
    
    public IBingWebSearchBuilder WithFreshness(BingFreshness freshness)
    {
        _config.Freshness = freshness;
        return this;
    }
    
    public IBingWebSearchBuilder WithResponseFilter(string filter)
    {
        _config.ResponseFilter = filter;
        return this;
    }
    
    public IBingWebSearchBuilder EnableShoppingSearch(bool enable = true)
    {
        _config.ShoppingSearch = enable;
        return this;
    }
    
    public IBingWebSearchBuilder WithTextDecorations(bool enable = true)
    {
        _config.TextDecorations = enable;
        return this;
    }
    
    public IBingWebSearchBuilder WithTextFormat(BingTextFormat format)
    {
        _config.TextFormat = format;
        return this;
    }
    
    
    IWebSearchConnector IWebSearchProviderBuilder<IBingWebSearchBuilder>.Build()
    {
        // If no API key was provided manually, try to resolve it from configuration
        if (string.IsNullOrWhiteSpace(_config.ApiKey))
        {
            _config.ApiKey = _configuration?["Bing:ApiKey"] ?? string.Empty;
        }
        // Apply fixed defaults for Bing
        if (!_config.Timeout.HasValue) _config.Timeout = TimeSpan.FromSeconds(30);
        if (string.IsNullOrWhiteSpace(_config.Market)) _config.Market = "en-US";

        _config.Validate();
        // TODO: Implement BingConnector
        throw new NotImplementedException("BingConnector implementation coming in next iteration");
    }
}
