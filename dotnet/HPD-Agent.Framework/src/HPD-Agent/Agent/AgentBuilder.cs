using HPD.Agent.Providers;
using HPD.Agent.Middleware;
using HPD.Agent.Middleware.Function;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using HPD.Agent.Validation;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Caching.Distributed;
using System.Text.Json;
using HPD.Agent;
using HPD.Agent.Secrets;
using HPD.Agent.Serialization;

namespace HPD.Agent;

// NOTE: Project Middleware classes are defined in the global namespace with the Project class

/// <summary>
/// Dependencies needed for agent construction
/// </summary>
internal record AgentBuildDependencies(
    AgentClientSet ClientSet,
    ChatOptions? MergedOptions,
    ErrorHandling.IProviderErrorHandler ErrorHandler,
    /// <summary>
    /// HttpClients created by AgentBuilder for OpenAPI sources that did not provide their own.
    /// Transferred to Agent and disposed when Agent.Dispose() is called.
    /// </summary>
    IReadOnlyList<HttpClient>? OwnedHttpClients = null)
{
    public IChatClient? ClientToUse => ClientSet.Chat;
    public IChatClient? SummarizerClient => ClientSet.Summarizer;
}

internal record AgentToolBuildResult(
    ChatOptions? MergedOptions,
    IReadOnlyList<HttpClient>? OwnedHttpClients = null);

/// <summary>
/// Builder for creating dual interface agents with sophisticated capabilities
/// This is your equivalent of the AgentBuilder from Semantic Kernel, but for the new architecture
/// </summary>
public class AgentBuilder
{
    private sealed class CompositeDisposable(params IDisposable[] disposables) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
                return;

            foreach (var disposable in disposables)
                disposable.Dispose();
        }
    }

    // The new central configuration object
    private readonly AgentConfig _config;
    private readonly IProviderRegistry _providerRegistry;

    // Fields that are NOT part of the serializable config remain
    internal IChatClient? _baseClient;
    internal IConfiguration? _configuration;
    internal IToolMetadata? _defaulTMetadata;
    internal bool _deferredProvider; // Skip provider validation - chat client will be provided at runtime

    /// <summary>
    /// Instance-based registrations for DI-required ToolHarnesses (e.g., AgentPlanToolHarness, DynamicMemoryToolHarness).
    /// These ToolHarnesses cannot be instantiated via the catalog because they require constructor parameters.
    /// </summary>
    public readonly List<ToolInstanceRegistration> _instanceRegistrations = new();
    // store individual ToolHarness contexts
    internal readonly Dictionary<string, IToolMetadata?> _toolharnessContexts = new();
    //  Unified content store for all agent content (skills, knowledge, memory, uploads, artifacts)
    internal IContentStore? _contentStore;
    // Track explicitly registered ToolHarnesses (for Collapsing manager)
    internal readonly HashSet<string> _explicitlyRegisteredToolHarnesses = new(StringComparer.OrdinalIgnoreCase);
    internal readonly List<Middleware.IAgentMiddleware> _middlewares = new(); // Unified middleware list
    internal readonly HPD.Agent.Permissions.PermissionOverrideRegistry _permissionOverrides = new(); // Permission overrides

    // Logging configuration - stored here and applied LAST in RegisterAutoMiddleware
    private LoggingMiddlewareOptions? _loggingOptions = null;

    // Function Collapse tracking for middleware Collapsing
    internal readonly Dictionary<string, string> _functionToToolHarnessMap = new(); // functionName -> toolTypeName
    internal readonly Dictionary<string, string> _functionToSkillMap = new(); // functionName -> skillName

    // Internal subscriptions for agent-level observability (developer-only, hidden from users)
    private readonly List<Func<HPD.Events.IEventCoordinator, IDisposable>> _eventSubscriptionFactories = new();

    internal readonly Dictionary<Type, object> _providerConfigs = new();
    internal IServiceProvider? _serviceProvider;
    private JsonSerializerOptions? _toolSerializerOptions;
    internal ILoggerFactory? _logger;

    // MCP runtime fields (stored as object to avoid circular reference to HPD-Agent.MCP)
    internal object? _mcpClientManager;

    // AIContextProvider factory (protocol-specific, stored as object for extensibility)
    internal object? _contextProviderFactory;

    //
    // AOT-COMPATIBLE ToolHarness REGISTRY (Phase: AOT ToolHarness Registry Hybrid)
    //
    // These fields enable reflection-free ToolHarness instantiation in hot paths.
    // The source generator creates a ToolRegistry.All array with direct delegates.

    /// <summary>
    /// ToolHarness catalog loaded from generated ToolHarnessRegistry.All.
    /// Starts with the calling assembly's registry and lazily loads additional assemblies
    /// when ToolHarnesses from other assemblies are requested via WithToolHarness&lt;T&gt;().
    /// </summary>
    internal readonly Dictionary<string, ToolHarnessFactory> _availableToolHarnesses = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Tracks which assemblies have already been scanned for ToolHarness registries.
    /// Used to avoid repeated reflection calls for the same assembly.
    /// </summary>
    internal readonly HashSet<Assembly> _loadedAssemblies = new();

    // Secret resolution
    private ISecretResolver? _secretResolver;
    private readonly List<ISecretResolver> _additionalResolvers = new();

    /// <summary>
    /// Selected ToolHarnesses for this agent (from WithToolHarness calls).
    /// Only ToolHarnesses in this list will have their functions created during Build().
    /// </summary>
    internal readonly List<ToolHarnessFactory> _selectedToolHarnessFactories = new();

    /// <summary>
    /// ToolHarness overrides from builder calls (takes precedence over config).
    /// Maps toolharness name -> ToolHarnessReference with updated config/metadata.
    /// Used for Config = Base, Builder = Override/Extend pattern.
    /// </summary>
    internal readonly Dictionary<string, ToolHarnessReference> _toolharnessOverrides = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Tracks which toolharnesses were added via builder (not config).
    /// Used to determine what's an override vs extension.
    /// </summary>
    internal readonly HashSet<string> _builderAddedToolHarnesses = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Builder-time DI middleware instances for toolharness-scoped middleware .
    /// Maps toolharness name -> list of middleware instances supplied via
    /// <c>WithToolHarness&lt;T&gt;(opts => opts.AddScopedMiddleware(...))</c>.
    /// Merged with attribute-declared factory middlewares at container expansion time.
    /// </summary>
    internal readonly Dictionary<string, List<Middleware.IAgentMiddleware>> _HARNESScopedMiddlewares
        = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Middleware overrides from builder calls (takes precedence over config).
    /// Maps middleware type -> middleware instance.
    /// Used for Config = Base, Builder = Override/Extend pattern.
    /// </summary>
    internal readonly Dictionary<Type, IAgentMiddleware> _middlewareOverrides = new();

    /// <summary>
    /// Tracks middleware types that were resolved from config.
    /// Used to detect override vs extend scenarios.
    /// </summary>
    internal readonly HashSet<Type> _configMiddlewareTypes = new();

    /// <summary>
    /// Function filters for Phase 4.5 selective registration.
    /// Maps ToolHarness name -> array of function names to include.
    /// When a ToolHarness is auto-registered as a skill dependency, only these functions are included.
    /// </summary>
    internal readonly Dictionary<string, string[]> _toolFunctionFilters = new(StringComparer.OrdinalIgnoreCase);

    //
    // AOT-COMPATIBLE MIDDLEWARE REGISTRY (Phase: Config Serialization)
    //
    // These fields enable reflection-free middleware instantiation in hot paths.
    // The source generator creates a MiddlewareRegistry.All array with direct delegates.

    /// <summary>
    /// Middleware catalog loaded from generated MiddlewareRegistry.All.
    /// Starts with the calling assembly's registry and lazily loads additional assemblies.
    /// </summary>
    internal readonly Dictionary<string, Middleware.MiddlewareFactory> _availableMiddlewares = new(StringComparer.OrdinalIgnoreCase);

    //
    // AOT-COMPATIBLE MIDDLEWARE STATE REGISTRY (Phase: Cross-Assembly State Discovery)
    //
    // These fields enable cross-assembly middleware state discovery following the ToolHarnessRegistry pattern.
    // Each assembly generates a MiddlewareStateRegistry.All array with factories for [MiddlewareState] types.

    /// <summary>
    /// Middleware state catalog loaded from generated MiddlewareStateRegistry.All.
    /// Starts with the calling assembly's registry and lazily loads additional assemblies
    /// when toolharnesses from other assemblies are registered via WithToolHarness&lt;T&gt;().
    /// </summary>
    internal readonly Dictionary<string, MiddlewareStateFactory> _stateFactories = new(StringComparer.Ordinal);

    /// <summary>
    /// Tracks which assemblies have already been scanned for state registries.
    /// Used to avoid repeated reflection calls for the same assembly.
    /// </summary>
    internal readonly HashSet<Assembly> _loadedStateAssemblies = new();

    /// <summary>
    /// ToolHarness configs from config ToolHarnesses list.
    /// Maps toolharness name -> JsonElement config for CreateFromConfig delegate.
    /// </summary>
    internal readonly Dictionary<string, System.Text.Json.JsonElement> _toolharnessConfigs = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Per-toolharness middleware config overrides from <c>ToolHarnessReference.MiddlewareConfigs</c> .
    /// Maps toolharness name -> (middleware simple type name -> JsonElement config).
    /// Passed to <see cref="ContainerMiddleware"/> at build time for config-constructor middleware instantiation.
    /// </summary>
    internal readonly Dictionary<string, Dictionary<string, System.Text.Json.JsonElement>> _toolharnessMiddlewareConfigs
        = new(StringComparer.OrdinalIgnoreCase);

    // ==========  OpenAPI Support  ==========

    /// <summary>
    /// Static singleton set by OpenApiAutoDiscovery.Initialize() via [ModuleInitializer].
    /// Null until HPD-Agent.OpenApi is loaded. Same pattern as provider modules.
    /// </summary>
    private static IOpenApiLoader? s_openApiLoader;
    private static IMcpToolLoader? s_mcpToolLoader;

    /// <summary>
    /// OpenAPI sources registered via WithOpenApi() or [OpenApi] toolharness attribute.
    /// Resolved and loaded in BuildDependenciesAsync() after MCP tool loading.
    /// </summary>
    internal readonly List<OpenApiSourceRegistration> _openApiSources = new();

    /// <summary>
    /// Registers the OpenAPI loader hook. Called by OpenApiAutoDiscovery via [ModuleInitializer].
    /// Thread-safe — [ModuleInitializer] methods are called at most once.
    /// </summary>
    internal static void RegisterOpenApiLoader(IOpenApiLoader loader)
    {
        s_openApiLoader = loader;
    }

    /// <summary>
    /// Registers the MCP loader hook. Called by MCPAutoDiscovery via [ModuleInitializer].
    /// </summary>
    internal static void RegisterMcpToolLoader(IMcpToolLoader loader)
    {
        s_mcpToolLoader = loader;
    }

    /// <summary>
    /// Adds a pending OpenAPI source registration. Called by WithOpenApi() and
    /// CreateFunctionsFromCatalog() when a toolharness has [OpenApi] methods.
    /// </summary>
    internal void AddOpenApiSource(OpenApiSourceRegistration registration)
    {
        _openApiSources.Add(registration);
    }

    /// <summary>
    /// Creates a new builder with default configuration.
    /// Provider assemblies are automatically discovered via ProviderAutoDiscovery ModuleInitializer.
    /// </summary>
    public AgentBuilder()
    {
        _config = new AgentConfig();
        _providerRegistry = new ProviderRegistry();

        LoadGeneratedRegistries();
        RegisterDiscoveredProviders();
    }

    /// <summary>
    /// Creates a builder from existing configuration.
    /// Provider assemblies are automatically discovered via ProviderAutoDiscovery ModuleInitializer.
    /// </summary>
    public AgentBuilder(AgentConfig config)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _providerRegistry = new ProviderRegistry();

        LoadGeneratedRegistries();
        RegisterDiscoveredProviders();
    }

    /// <summary>
    /// Creates a builder from a JSON or YAML agent configuration file.
    /// </summary>
    public AgentBuilder(string configFilePath)
        : this(LoadConfigFile(configFilePath))
    {
    }

    /// <summary>
    /// Creates a builder with custom provider registry (for testing).
    /// Optionally accepts an assembly hint for ToolHarness registry discovery.
    /// </summary>
    public AgentBuilder(AgentConfig config, IProviderRegistry providerRegistry)
    {
        _config = config;
        _providerRegistry = providerRegistry;

        LoadGeneratedRegistries();
    }

    /// <summary>
    /// Creates a builder from a JSON or YAML agent configuration file.
    /// </summary>
    public static AgentBuilder FromFile(string configFilePath)
        => new(configFilePath);

    private static AgentConfig LoadConfigFile(string configFilePath)
    {
        if (string.IsNullOrWhiteSpace(configFilePath))
            throw new ArgumentException("Configuration file path cannot be null or empty.", nameof(configFilePath));

        if (!File.Exists(configFilePath))
            throw new FileNotFoundException($"Configuration file not found: {configFilePath}");

        return HpdAgentConfigSerializer.ReadFile(configFilePath)
            ?? throw new JsonException($"Failed to deserialize AgentConfig from '{configFilePath}' - result was null.");
    }

    internal void LoadGeneratedRegistries()
    {
        var (toolharnesses, middlewares, states) = AgentGeneratedRegistry.Snapshot();

        foreach (var factory in toolharnesses)
            _availableToolHarnesses.TryAdd(factory.Name, factory);

        foreach (var factory in middlewares)
            _availableMiddlewares.TryAdd(factory.Name, factory);

        foreach (var factory in states)
            _stateFactories.TryAdd(factory.FullyQualifiedName, factory);
    }

    /// <summary>
    /// Registers all providers that were discovered by ProviderAutoDiscovery ModuleInitializer.
    /// Provider assemblies are loaded and their ModuleInitializers run before this is called.
    /// For PublishSingleFile scenarios, also force-loads provider assemblies from the calling assembly.
    /// </summary>
    private void RegisterDiscoveredProviders()
    {
        // For PublishSingleFile, ModuleInitializers may not fire until assemblies are explicitly loaded
        // Force load provider assemblies referenced by the entry/calling assembly
        ForceLoadProviderAssembliesFromCallingAssembly();

        foreach (var factory in ProviderDiscovery.GetFactories())
        {
            try
            {
                var provider = factory();
                _providerRegistry.Register(provider);
            }
            catch (Exception ex)
            {
                _logger?.CreateLogger<AgentBuilder>().LogWarning(ex, "Failed to register provider from discovery");
            }
        }
    }

    /// <summary>
    /// Force-loads provider assemblies by trying to load known provider names.
    /// This triggers ModuleInitializers in PublishSingleFile scenarios.
    /// For PublishSingleFile, GetReferencedAssemblies() may not work reliably, so we try known names.
    /// </summary>
    private void ForceLoadProviderAssembliesFromCallingAssembly()
    {
        // Known provider assembly names to try loading
        string[] knownProviders = {
            "HPD-Agent.Providers.OpenRouter",
            "HPD-Agent.Providers.Anthropic",
            "HPD-Agent.Providers.Cohere",
            "HPD-Agent.Providers.Cerebras",
            "HPD-Agent.Providers.DeepSeek",
            "HPD-Agent.Providers.Xai",
            "HPD-Agent.Providers.SambaNova",
            "HPD-Agent.Providers.Hyperbolic",
            "HPD-Agent.Providers.OVHcloud",
            "HPD-Agent.Providers.Nscale",
            "HPD-Agent.Providers.Venice",
            "HPD-Agent.Providers.Perplexity",
            "HPD-Agent.Providers.LMStudio",
            "HPD-Agent.Providers.Nebius",
            "HPD-Agent.Providers.NvidiaNim",
            "HPD-Agent.Providers.SiliconFlow",
            "HPD-Agent.Providers.Scaleway",
            "HPD-Agent.Providers.Zai",
            "HPD-Agent.Providers.MiniMax",
            "HPD-Agent.Providers.AzureAI",
            "HPD-Agent.Providers.OpenAI",
            "HPD-Agent.Providers.Ollama",
            "HPD-Agent.Providers.GoogleAI",
            "HPD-Agent.Providers.HuggingFace",
            "HPD-Agent.Providers.Bedrock",
            "HPD-Agent.Providers.Mistral",
            "HPD-Agent.Providers.OnnxRuntime",
            "HPD.Agent.Audio",
            "HPD-Agent.Providers.Audio.OpenAI",
            "HPD-Agent.Providers.Audio.ElevenLabs",
            "HPD-Agent.Providers.Audio.Meai",
            "HPD-Agent.Providers.Audio.Silero"
        };

        foreach (var providerName in knownProviders)
        {
            try
            {
                // Try to load the assembly by name - if it's referenced, this will load it
                var assembly = Assembly.Load(new AssemblyName(providerName));
                if (assembly != null)
                {
                    // Trigger the module initializer
                    RuntimeHelpers.RunModuleConstructor(assembly.ManifestModule.ModuleHandle);
                }
            }
            catch
            {
                // Ignore - provider not referenced/available in this application
            }
        }
    }


    /// <summary>
    /// Loads ToolHarnesses, Middlewares, and Middleware States from the generated registries in the specified assembly
    /// and merges them into the _availableToolHarnesses, _availableMiddlewares, and _stateFactories dictionaries.
    /// Uses minimal reflection (GetType calls per assembly) to discover the catalogs.
    /// Thread-safe: tracks loaded assemblies to avoid duplicate processing.
    /// WARNING: Requires source-generated registry types to be preserved in AOT.
    /// </summary>
    /// <param name="assembly">The assembly to search for the generated registries</param>
    [RequiresUnreferencedCode("Registry lookup via Assembly.GetType requires ToolHarnessRegistry, MiddlewareRegistry, and MiddlewareStateRegistry types to be preserved during AOT compilation.")]
    internal void LoadToolRegistryFromAssembly(Assembly assembly)
    {
        // Skip if already loaded
        if (!_loadedAssemblies.Add(assembly))
        {
            return;
        }

        // Load ToolHarness registry
        LoadToolHarnessRegistryFromAssembly(assembly);

        // Load Middleware registry
        LoadMiddlewareRegistryFromAssembly(assembly);

        // Load Middleware State registry (cross-assembly state discovery)
        LoadStateRegistryFromAssembly(assembly);
    }

    /// <summary>
    /// Loads ToolHarnesses from the generated ToolHarnessRegistry.All in the specified assembly.
    /// </summary>
    [RequiresUnreferencedCode("ToolHarness registry lookup via Assembly.GetType requires ToolHarnessRegistry type to be preserved during AOT compilation.")]
    private void LoadToolHarnessRegistryFromAssembly(Assembly assembly)
    {
        try
        {
            // ONE reflection call: Look for generated registry in the specified assembly
            // This type name is a constant known at compile time, making it AOT-safe
            var registryType = assembly.GetType("HPD.Agent.Generated.ToolHarnessRegistry");

            if (registryType == null)
            {
                // No registry found - no ToolHarnesses available from this assembly
                return;
            }

            // Get the All field (static readonly array - NOT a property!)
            var allField = registryType.GetField("All", BindingFlags.Public | BindingFlags.Static);
            if (allField == null)
            {
                return;
            }

            // Get the ToolHarnessFactory array
            var factories = allField.GetValue(null) as ToolHarnessFactory[];
            if (factories == null || factories.Length == 0)
            {
                return;
            }

            // Add to dictionary (new ToolHarnesses from this assembly)
            foreach (var factory in factories)
            {
                // Use TryAdd to avoid overwriting if ToolHarness with same name already exists
                _availableToolHarnesses.TryAdd(factory.Name, factory);
            }
        }
        catch (Exception ex)
        {
            // Log warning but don't crash - no ToolHarnesses from this assembly
            _logger?.CreateLogger<AgentBuilder>()
                .LogWarning(ex, "Failed to load ToolHarnessRegistry.All from assembly {Assembly}", assembly.FullName);
        }
    }

    /// <summary>
    /// Loads Middlewares from the generated MiddlewareRegistry.All in the specified assembly.
    /// </summary>
    [RequiresUnreferencedCode("Middleware registry lookup via Assembly.GetType requires MiddlewareRegistry type to be preserved during AOT compilation.")]
    private void LoadMiddlewareRegistryFromAssembly(Assembly assembly)
    {
        try
        {
            // Look for generated middleware registry in the specified assembly
            var registryType = assembly.GetType("HPD.Agent.Generated.MiddlewareRegistry");

            if (registryType == null)
            {
                // No middleware registry found - no middlewares available from this assembly
                return;
            }

            // Get the All field (static readonly array)
            var allField = registryType.GetField("All", BindingFlags.Public | BindingFlags.Static);
            if (allField == null)
            {
                return;
            }

            // Get the MiddlewareFactory array
            var factories = allField.GetValue(null) as Middleware.MiddlewareFactory[];
            if (factories == null || factories.Length == 0)
            {
                return;
            }

            // Add to dictionary (new middlewares from this assembly)
            foreach (var factory in factories)
            {
                // Use TryAdd to avoid overwriting if middleware with same name already exists
                _availableMiddlewares.TryAdd(factory.Name, factory);
            }
        }
        catch (Exception ex)
        {
            // Log warning but don't crash - no middlewares from this assembly
            _logger?.CreateLogger<AgentBuilder>()
                .LogWarning(ex, "Failed to load MiddlewareRegistry.All from assembly {Assembly}", assembly.FullName);
        }
    }

    /// <summary>
    /// Loads middleware state factories from the generated MiddlewareStateRegistry.All in the specified assembly.
    /// This enables cross-assembly state discovery following the ToolHarnessRegistry pattern.
    /// </summary>
    [RequiresUnreferencedCode("State registry lookup via Assembly.GetType requires MiddlewareStateRegistry type to be preserved during AOT compilation.")]
    internal void LoadStateRegistryFromAssembly(Assembly assembly)
    {
        // Skip if already loaded for this builder instance
        if (!_loadedStateAssemblies.Add(assembly))
            return;

        try
        {
            // Look for generated state registry in the specified assembly
            var registryType = assembly.GetType("HPD.Agent.Generated.MiddlewareStateRegistry");

            if (registryType == null)
            {
                // No state registry found - no middleware states in this assembly
                return;
            }

            // Get the All field (static readonly array)
            var allField = registryType.GetField("All", BindingFlags.Public | BindingFlags.Static);
            if (allField == null)
            {
                return;
            }

            // Get the MiddlewareStateFactory array
            var factories = allField.GetValue(null) as MiddlewareStateFactory[];
            if (factories == null || factories.Length == 0)
            {
                return;
            }

            // Add to dictionary (new state factories from this assembly)
            foreach (var factory in factories)
            {
                // Use TryAdd to avoid overwriting if state with same key already exists
                _stateFactories.TryAdd(factory.FullyQualifiedName, factory);
            }
        }
        catch (Exception ex)
        {
            // Log warning but don't crash - no states from this assembly
            _logger?.CreateLogger<AgentBuilder>()
                .LogWarning(ex, "Failed to load MiddlewareStateRegistry.All from assembly {Assembly}", assembly.FullName);
        }
    }

    /// <summary>
    /// Creates AIFunctions from selected ToolHarnesses using the catalog (zero reflection in hot path).
    /// Also handles instance-based registrations for DI ToolHarnesses.
    /// Phase 4.5: Applies function filters for selective registration.
    /// </summary>
    /// <returns>List of AIFunctions from all selected ToolHarnesses</returns>
    private List<AIFunction> CreateFunctionsFromCatalog()
    {
        var allFunctions = new List<AIFunction>();

        // Process catalog-based ToolHarnesses (zero reflection in hot path)
        foreach (var factory in _selectedToolHarnessFactories)
        {
            try
            {
                _toolharnessContexts.TryGetValue(factory.Name, out var ctx);

                // Create ToolHarness instance using AOT-safe resolution:
                // 1. Try DI first (if ServiceProvider available)
                // 2. Try config-based instantiation (if config provided)
                // 3. Try ISecretResolver-only constructor (auto-injected from builder)
                // 4. Fall back to parameterless constructor
                object instance;

                // 1. Try DI first
                if (_serviceProvider != null)
                {
                    var diInstance = _serviceProvider.GetService(factory.ToolHarnessType);
                    if (diInstance != null)
                    {
                        instance = diInstance;
                        goto HaveInstance;
                    }
                }

                // 2. Try config-based instantiation
                if (_toolharnessConfigs.TryGetValue(factory.Name, out var config) && factory.CreateFromConfig != null)
                {
                    instance = factory.CreateFromConfig(config);
                    goto HaveInstance;
                }

                // 3. Auto-inject ISecretResolver (ZERO REFLECTION - direct delegate call!)
                if (factory.CreateWithSecrets != null && _secretResolver != null)
                {
                    instance = factory.CreateWithSecrets(_secretResolver);
                    goto HaveInstance;
                }

                // 4. Fall back to parameterless constructor (ZERO REFLECTION - direct delegate call!)
                instance = factory.CreateInstance();

            HaveInstance:
                // Collect OpenAPI sources from [OpenApi] toolharness methods (ZERO REFLECTION!)
                // Config is stored as object; cast to OpenApiConfig happens in OpenApiLoader.
                // CollapseWithinToolHarness placeholder is false — loader reads it from config directly.
                if (factory.CollectOpenApiSources != null)
                {
                    factory.CollectOpenApiSources(instance, (name, config, parentContainer) =>
                    {
                        _openApiSources.Add(new OpenApiSourceRegistration(
                            Name: name,
                            ParentContainer: parentContainer,
                            CollapseWithinToolHarness: false,   // placeholder — loader reads from config
                            Config: config));
                    });
                }

                // Call CreateFunctions delegate (ZERO REFLECTION!)
                var functions = factory.CreateFunctions(instance, ctx ?? _defaulTMetadata, CreateToolSerializationOptions());

                // Phase 4.5: Apply function filter if this ToolHarness has selective registration
                if (_toolFunctionFilters.TryGetValue(factory.Name, out var functionFilter))
                {
                    // Only include functions that are in the filter
                    functions = functions
                        .Where(f => functionFilter.Contains(f.Name))
                        .ToList();
                }

                allFunctions.AddRange(functions);
            }
            catch (Exception ex)
            {
                _logger?.CreateLogger<AgentBuilder>()
                    .LogWarning(ex, "Failed to create functions for ToolHarness {ToolHarnessName}", factory.Name);
            }
        }

        // Process instance-based registrations (for DI ToolHarnesses like AgentPlanToolHarness, DynamicMemoryToolHarness)
        foreach (var registration in _instanceRegistrations)
        {
            try
            {
                if (!_availableToolHarnesses.TryGetValue(registration.ToolTypeName, out var factory))
                {
                    throw new InvalidOperationException(
                        $"ToolHarness '{registration.ToolTypeName}' not found in ToolHarnessRegistry.All. " +
                        $"Ensure the toolharness class has [AIFunction], [Skill], or [SubAgent] attributes and the source generator ran successfully.");
                }

                _toolharnessContexts.TryGetValue(registration.ToolTypeName, out var ctx);

                if (factory.CollectOpenApiSources != null)
                {
                    factory.CollectOpenApiSources(registration.Instance, (name, config, parentContainer) =>
                    {
                        _openApiSources.Add(new OpenApiSourceRegistration(
                            Name: name,
                            ParentContainer: parentContainer,
                            CollapseWithinToolHarness: false,
                            Config: config));
                    });
                }

                var functions = factory.CreateFunctions(registration.Instance, ctx ?? _defaulTMetadata, CreateToolSerializationOptions());

                // Apply function filter if set
                if (registration.FunctionFilter != null && registration.FunctionFilter.Length > 0)
                {
                    functions = functions
                        .Where(f => registration.FunctionFilter.Contains(f.Name))
                        .ToList();
                }

                allFunctions.AddRange(functions);
            }
            catch (Exception ex)
            {
                _logger?.CreateLogger<AgentBuilder>()
                    .LogWarning(ex, "Failed to create functions for instance ToolHarness {ToolHarnessName}", registration.ToolTypeName);
            }
        }

        return allFunctions;
    }

    private HPDToolSerializationOptions? CreateToolSerializationOptions()
    {
        return _toolSerializerOptions is null
            ? null
            : new HPDToolSerializationOptions(_toolSerializerOptions);
    }

    /// <summary>
    /// Resolves toolharnesses from config ToolHarnesses list and adds them to _selectedToolHarnessFactories.
    /// Implements Config = Base, Builder = Override/Extend pattern:
    /// - Config toolharnesses are registered first (in order)
    /// - Builder calls can override (replace same name) or extend (add new)
    /// - DI-first resolution: try ServiceProvider first, fall back to CreateInstance
    /// </summary>
    private void ResolveConfigToolHarnesses()
    {
        if (_config.ToolHarnesses == null || _config.ToolHarnesses.Count == 0)
            return;

        LoadGeneratedRegistries();

        var logger = _logger?.CreateLogger<AgentBuilder>();
        logger?.LogDebug("Resolving {Count} toolharnesses from config", _config.ToolHarnesses.Count);

        foreach (var toolharnessRef in _config.ToolHarnesses)
        {
            // Check if builder has an override for this toolharness
            var effectiveRef = _toolharnessOverrides.TryGetValue(toolharnessRef.Name, out var ovr)
                ? ovr
                : toolharnessRef;

            // Skip if already added via builder (it will be in _selectedToolHarnessFactories already)
            if (_builderAddedToolHarnesses.Contains(effectiveRef.Name))
            {
                logger?.LogDebug("ToolHarness '{Name}' already registered via builder, skipping config", effectiveRef.Name);
                continue;
            }

            // Look up in available toolharnesses
            if (!_availableToolHarnesses.TryGetValue(effectiveRef.Name, out var factory))
            {
                logger?.LogWarning(
                    "ToolHarness '{Name}' referenced in config not found in registry. " +
                    "Ensure the class has [AIFunction], [Skill], or [SubAgent] methods and a parameterless constructor.",
                    effectiveRef.Name);
                continue;
            }

            // Check if already selected (avoid duplicates)
            if (_selectedToolHarnessFactories.Any(f => f.Name.Equals(factory.Name, StringComparison.OrdinalIgnoreCase)))
            {
                logger?.LogDebug("ToolHarness '{Name}' already selected, skipping duplicate", effectiveRef.Name);
                continue;
            }

            // Add to selected factories
            _selectedToolHarnessFactories.Add(factory);
            _explicitlyRegisteredToolHarnesses.Add(factory.Name);

            // Handle function filtering from config
            if (effectiveRef.Functions != null && effectiveRef.Functions.Count > 0)
            {
                _toolFunctionFilters[factory.Name] = effectiveRef.Functions.ToArray();
            }

            // Handle config-based instantiation (store config for CreateFunctionsFromCatalog)
            if (effectiveRef.Config.HasValue && factory.CreateFromConfig != null)
            {
                _toolharnessConfigs[factory.Name] = effectiveRef.Config.Value;
            }

            // Handle metadata from config
            if (effectiveRef.Metadata.HasValue && factory.DeserializeMetadata != null)
            {
                try
                {
                    var metadata = factory.DeserializeMetadata(effectiveRef.Metadata.Value);
                    if (metadata != null)
                    {
                        _toolharnessContexts[factory.Name] = metadata;
                    }
                }
                catch (JsonException ex)
                {
                    logger?.LogWarning(ex,
                        "Failed to deserialize metadata for toolharness '{Name}' to type {MetadataType}",
                        effectiveRef.Name, factory.MetadataType?.Name ?? "unknown");
                }
            }

            // Handle middleware configs from config (§5A)
            if (effectiveRef.MiddlewareConfigs != null && effectiveRef.MiddlewareConfigs.Count > 0
                && factory.CollapseMiddlewareConfigFactories != null)
            {
                _toolharnessMiddlewareConfigs[factory.Name] = effectiveRef.MiddlewareConfigs;
            }

            logger?.LogDebug("Resolved toolharness '{Name}' from config", effectiveRef.Name);
        }
    }

    /// <summary>
    /// Registers a toolharness override from builder.
    /// Called by WithToolHarness extension methods when using config + builder pattern.
    /// </summary>
    public AgentBuilder WithToolHarnessOverride(ToolHarnessReference reference)
    {
        _toolharnessOverrides[reference.Name] = reference;
        return this;
    }

    /// <summary>
    /// Resolves middlewares from config Middlewares list and adds them to _middlewares.
    /// Implements Config = Base, Builder = Override/Extend pattern:
    /// - Config middlewares are registered first (in order)
    /// - Builder calls can override (replace same type) or extend (add new type)
    /// Uses source-generated MiddlewareRegistry for AOT-compatible resolution.
    /// </summary>
    private void ResolveConfigMiddlewares()
    {
        if (_config.Middlewares == null || _config.Middlewares.Count == 0)
            return;

        var logger = _logger?.CreateLogger<AgentBuilder>();
        logger?.LogDebug("Resolving {Count} middlewares from config", _config.Middlewares.Count);

        foreach (var middlewareRef in _config.Middlewares)
        {
            // Try to resolve middleware from source-generated registry (AOT-safe)
            if (!_availableMiddlewares.TryGetValue(middlewareRef.Name, out var factory))
            {
                logger?.LogWarning(
                    "Middleware '{Name}' referenced in config not found in registry. " +
                    "Ensure the class has [Middleware] attribute and implements IAgentMiddleware.",
                    middlewareRef.Name);
                continue;
            }

            var middlewareType = factory.MiddlewareType;

            // Check if builder has an override for this type
            if (_middlewareOverrides.TryGetValue(middlewareType, out var overrideInstance))
            {
                // Builder override takes precedence
                if (!_middlewares.Any(m => m.GetType() == middlewareType))
                {
                    _middlewares.Add(overrideInstance);
                }
                _configMiddlewareTypes.Add(middlewareType);
                logger?.LogDebug("Middleware '{Name}' overridden by builder", middlewareRef.Name);
                continue;
            }

            // Create middleware instance using AOT-safe resolution
            try
            {
                IAgentMiddleware? instance = null;

                // 1. Try DI first (supports constructor injection for complex middlewares)
                if (_serviceProvider != null)
                {
                    instance = _serviceProvider.GetService(middlewareType) as IAgentMiddleware;
                    if (instance != null)
                    {
                        logger?.LogDebug("Middleware '{Name}' resolved from DI", middlewareRef.Name);
                    }
                }

                // 2. Try config-based instantiation (if config provided and factory supports it)
                if (instance == null && middlewareRef.Config.HasValue && factory.CreateFromConfig != null)
                {
                    instance = factory.CreateFromConfig(middlewareRef.Config.Value);
                    logger?.LogDebug("Middleware '{Name}' instantiated from config", middlewareRef.Name);
                }

                // 3. Fall back to parameterless constructor (AOT-safe, no Activator.CreateInstance!)
                if (instance == null && factory.CreateInstance != null)
                {
                    instance = factory.CreateInstance();
                    logger?.LogDebug("Middleware '{Name}' instantiated with parameterless constructor", middlewareRef.Name);
                }

                if (instance != null)
                {
                    _middlewares.Add(instance);
                    _configMiddlewareTypes.Add(middlewareType);
                    logger?.LogDebug("Resolved middleware '{Name}' from config", middlewareRef.Name);
                }
                else if (factory.RequiresDI)
                {
                    logger?.LogWarning(
                        "Middleware '{Name}' requires DI. Register via services.AddTransient<{Type}>().",
                        middlewareRef.Name, middlewareType.Name);
                }
                else
                {
                    logger?.LogWarning("Failed to create instance of middleware '{Name}'", middlewareRef.Name);
                }
            }
            catch (Exception ex)
            {
                logger?.LogWarning(ex, "Failed to instantiate middleware '{Name}'", middlewareRef.Name);
            }
        }
    }

    /// <summary>
    /// Resolves a middleware factory by name from the source-generated registry.
    /// Returns the MiddlewareFactory if found, or null if not in registry.
    /// </summary>
    private Middleware.MiddlewareFactory? ResolveMiddlewareFactory(string name)
    {
        // Try exact match first
        if (_availableMiddlewares.TryGetValue(name, out var factory))
            return factory;

        // Try with "Middleware" suffix
        if (_availableMiddlewares.TryGetValue($"{name}Middleware", out factory))
            return factory;

        return null;
    }

    /// <summary>
    /// Sets the system instructions/persona for the agent
    /// </summary>
    public AgentBuilder WithInstructions(string instructions)
    {
        _config.SystemInstructions = instructions ?? throw new ArgumentNullException(nameof(instructions));
        return this;
    }

    // ═══════════════════════════════════════════════════════════════════
    //  Unified Content Store & Provider Registry
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Configure a custom content store for this agent.
    /// The store provides framework-managed runtime content storage for uploads,
    /// internal references, and artifacts.
    /// </summary>
    public AgentBuilder WithContentStore(IContentStore store)
    {
        _contentStore = store ?? throw new ArgumentNullException(nameof(store));
        return this;
    }

    /// <summary>
    /// Configure the provider registry for intelligent content upload routing.
    /// When set, ContentUploadMiddleware can automatically detect provider capabilities
    /// and route DataContent uploads to HostedFileClient (provider-native) or IContentStore.
    /// </summary>
    public AgentBuilder WithProviderRegistry(IProviderRegistry registry)
    {
        // _providerRegistry is readonly, so this is a noop setter for now
        // In future, we may make it settable or use dependency injection
        return this;
    }

    /// <summary>
    /// Provides the service provider for resolving dependencies.
    /// Required for:
    /// - Observability: ILoggerFactory (for structured logging), IDistributedCache (for response caching)
    /// - Contextual functions: Embedding generators via UseRegisteredEmbeddingGenerator()
    /// </summary>
    public AgentBuilder WithServiceProvider(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
        _logger = serviceProvider.GetService<ILoggerFactory>();
        return this;
    }

    /// <summary>
    /// Replaces the entire secret resolver chain with a custom resolver.
    /// </summary>
    public AgentBuilder WithSecretResolver(ISecretResolver resolver)
    {
        _secretResolver = resolver;
        return this;
    }

    /// <summary>
    /// Adds a resolver to the default chain.
    /// Inserted after env vars, before IConfiguration.
    /// Use for vault resolvers, custom secret sources, or CLI auth storage.
    /// </summary>
    public AgentBuilder AddSecretResolver(ISecretResolver resolver)
    {
        _additionalResolvers.Add(resolver);
        return this;
    }

    /// <summary>
    /// Gets the configured secret resolver (available after Build).
    /// Exposed so toolharnesses and connectors can resolve secrets.
    /// </summary>
    public ISecretResolver? SecretResolver => _secretResolver;

    /// <summary>
    /// Configures the maximum number of turns the agent can take to call functions before requiring continuation permission
    /// </summary>
    /// <param name="maxTurns">Maximum number of function-calling turns (default: 10)</param>
    public AgentBuilder WithMaxFunctionCallTurns(int maxTurns)
    {
        if (maxTurns <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxTurns), "Maximum function call turns must be greater than 0");

        _config.MaxAgenticIterations = maxTurns;
        return this;
    }

    /// <summary>
    /// Configures how many additional turns to allow when user chooses to continue beyond the limit
    /// </summary>
    /// <param name="extensionAmount">Additional turns to allow (default: 3)</param>
    public AgentBuilder WithContinuationExtensionAmount(int extensionAmount)
    {
        if (extensionAmount <= 0)
            throw new ArgumentOutOfRangeException(nameof(extensionAmount), "Continuation extension amount must be greater than 0");

        _config.ContinuationExtensionAmount = extensionAmount;
        return this;
    }

    //
    // PROTOCOL-SPECIFIC CONFIGURATION
    //
    // Protocol-specific configuration methods (WithContextProviderFactory, etc.) are now
    // provided via extension methods in protocol adapter projects (HPD-Agent.Microsoft, etc.)

    /// <summary>
    /// Internal method to set protocol-specific context provider factory.
    /// Used by protocol adapter extension methods (e.g., HPD.Agent.Microsoft.AgentBuilderExtensions).
    /// </summary>
    internal void SeTMetadataProviderFactory(object factory)
    {
        _contextProviderFactory = factory ?? throw new ArgumentNullException(nameof(factory));
    }

    /// <summary>
    /// Internal method to get protocol-specific context provider factory.
    /// Used by protocol adapter extension methods to retrieve the stored factory.
    /// </summary>
    internal object? GeTMetadataProviderFactory() => _contextProviderFactory;

    //
    // DUAL-LAYER OBSERVABILITY ARCHITECTURE
    //
    // HPD-Agent implements a dual-layer observability model that combines:
    // 1. LLM-level instrumentation (Microsoft.Extensions.AI middleware)
    // 2. Agent-level instrumentation (HPD's specialized services)
    //
    // ┌────────────────────────────────────────────────────────────────────────┐
    // │ LAYER 1: LLM-LEVEL OBSERVABILITY (Microsoft Middleware)               │
    // ├────────────────────────────────────────────────────────────────────────┤
    // │ • OpenTelemetryChatClient:                                             │
    // │   - Token usage histograms (prompt/completion/total tokens)            │
    // │   - Operation duration histograms (request latency)                    │
    // │   - Distributed traces (LLM call spans)                                │
    // │   - Gen AI Semantic Conventions v1.38 compliance                       │
    // │                                                                         │
    // │ • LoggingChatClient:                                                   │
    // │   - LLM invocation logging (GetResponseAsync/GetStreamingResponseAsync)│
    // │   - Request/response logging at Trace level (sensitive data)           │
    // │   - Error and cancellation logging                                     │
    // │                                                                         │
    // │ • DistributedCachingChatClient:                                        │
    // │   - Response caching with IDistributedCache (Redis, Memory, etc.)      │
    // │   - Cache key generation from messages + options                       │
    // │   - Streaming response coalescing                                      │
    // │                                                                         │
    // │ Applied: Automatically in AgentTurn.RunAsyncCore() on each LLM call    │
    // │ Wrapping: Base → Caching → Logging → Telemetry (Russian doll pattern) │
    // └────────────────────────────────────────────────────────────────────────┘
    //
    // ┌────────────────────────────────────────────────────────────────────────┐
    // │ LAYER 2: AGENT-LEVEL OBSERVABILITY (HPD Services)                     │
    // ├────────────────────────────────────────────────────────────────────────┤
    // │ • AgentTelemetryService:                                               │
    // │   - Agent decision tracking (CallLLM, Complete, Terminate)             │
    // │   - Circuit breaker trigger counting                                   │
    // │   - Iteration histograms per orchestration run                         │
    // │   - State-aware distributed tracing (AgentLoopState context)           │
    // │                                                                         │
    // │ • AgentLoggingService:                                                 │
    // │   - Agent decision logging with structured data                        │
    // │   - Circuit breaker warnings                                           │
    // │   - State snapshots at key orchestration points                        │
    // │   - Completion logging with iteration counts                           │
    // │                                                                         │
    // │ Applied: Created in Agent constructor, invoked throughout orchestration│
    // │ Collapse: Agent orchestration loop, not individual LLM calls              │
    // └────────────────────────────────────────────────────────────────────────┘
    //
    // WHY DUAL-LAYER?
    // ────────────────────────────────────────────────────────────────────────────
    // Microsoft middleware cannot access agent-specific context:
    //   ✗ Agent decisions (CallLLM vs Complete vs Terminate)
    //   ✗ Circuit breaker state
    //   ✗ Iteration tracking across multiple LLM calls
    //   ✗ AgentLoopState for rich contextual tracing
    //
    // HPD services cannot instrument LLM client internals:
    //   ✗ Token usage (requires IChatClient instrumentation)
    //   ✗ Provider-specific metadata (model, server address)
    //   ✗ Cache hit/miss tracking
    //
    // Together, they provide complete observability:
    //   ✓ "Why did the agent call the LLM?" (HPD)
    //   ✓ "What did the LLM say and how much did it cost?" (Microsoft)
    //   ✓ "Was the response cached?" (Microsoft)
    //   ✓ "How many iterations did the agent take?" (HPD)
    //
    // DEVELOPER EXPERIENCE:
    // ────────────────────────────────────────────────────────────────────────────
    // .WithTelemetry()  → Automatic: Microsoft middleware + HPD service
    // .WithLogging()    → Automatic: Microsoft middleware + HPD service
    // .WithCaching()    → Automatic: Microsoft middleware only
    //
    // Result: Zero boilerplate, production-grade observability at both layers.
    //

    /// <summary>
    /// Enables dual-layer telemetry tracking for complete observability:
    /// <list type="bullet">
    /// <item><description><b>LLM-level (Microsoft):</b> Token usage, duration, distributed traces for LLM calls</description></item>
    /// <item><description><b>Agent-level (HPD):</b> Decision tracking, circuit breaker, iteration histograms</description></item>
    /// </list>
    /// </summary>
    /// <param name="sourceName">ActivitySource/Meter name (default: "HPD.Agent")</param>
    /// <param name="enableSensitiveData">Include prompts/responses in traces (default: false)</param>
    /// <returns>The builder for chaining</returns>
    /// <remarks>
    /// <para>
    /// This method automatically registers both layers of telemetry:
    /// <list type="number">
    /// <item><description>Microsoft's <c>OpenTelemetryChatClient</c> middleware for LLM instrumentation</description></item>
    /// <item><description>HPD's <c>AgentTelemetryService</c> for agent orchestration instrumentation</description></item>
    /// </list>
    /// </para>
    /// <para><b>Requirements:</b> Call <c>WithServiceProvider()</c> with an <c>ILoggerFactory</c> registered.</para>
    /// <para><b>Metrics Emitted:</b></para>
    /// <list type="bullet">
    /// <item><description><c>gen_ai.client.token.usage</c> - Token consumption per LLM call (Microsoft)</description></item>
    /// <item><description><c>gen_ai.client.operation.duration</c> - LLM call latency (Microsoft)</description></item>
    /// <item><description><c>hpd.agent.decision.count</c> - Agent decisions (CallLLM/Complete/Terminate) (HPD)</description></item>
    /// <item><description><c>hpd.agent.circuit_breaker.triggered</c> - Circuit breaker activations (HPD)</description></item>
    /// <item><description><c>hpd.agent.iteration.count</c> - Iterations per orchestration run (HPD)</description></item>
    /// </list>
    /// </remarks>
    /// <example>
    /// <code>
    /// var agent = await new AgentBuilder()
    ///     .WithServiceProvider(services)  // ILoggerFactory required
    ///     .WithTelemetry(sourceName: "MyApp.Agent", enableSensitiveData: false)
    ///     .WithOpenAI(model: "gpt-4", apiKey: apiKey)
    ///     .BuildAsync();
    ///
    /// // Automatically instruments:
    /// // - LLM token usage, duration, traces (Microsoft)
    /// // - Agent decisions, iterations, circuit breaker (HPD)
    /// </code>
    /// </example>
    public AgentBuilder WithTelemetry(string? sourceName = null, bool enableSensitiveData = false)
    {
        var effectiveSourceName = sourceName ?? "HPD.Agent";

        // 1. Register Microsoft's OpenTelemetryChatClient middleware (user-facing LLM observability)
        // This provides LLM-level tracing (token usage, duration, model calls)
        this.UseChatClientMiddleware((client, services) =>
        {
            var loggerFactory = services?.GetService<ILoggerFactory>();
            var telemetryClient = new OpenTelemetryChatClient(
                client,
                loggerFactory?.CreateLogger(typeof(OpenTelemetryChatClient)),
                effectiveSourceName);

            telemetryClient.EnableSensitiveData = enableSensitiveData;

            return telemetryClient;
        });

        // 2. Internally create TelemetryEventObserver for agent-level observability (developer-only)
        // This tracks agent decisions, iterations, circuit breakers, etc.
        _eventSubscriptionFactories.Add(coordinator =>
        {
            var telemetryObserver = new TelemetryEventObserver(effectiveSourceName);
            return new CompositeDisposable(
                coordinator.Subscribe<AgentEvent>(telemetryObserver.HandleAsync),
                telemetryObserver);
        });

        return this;
    }

    /// <summary>
    /// Registers a <see cref="TracingObserver"/> that converts the agent event stream
    /// into OpenTelemetry <see cref="System.Diagnostics.Activity"/> spans.
    ///
    /// Produces three span types:
    /// <list type="bullet">
    /// <item><description><b>agent.turn</b> — one per user message (root span)</description></item>
    /// <item><description><b>agent.iteration</b> — one per LLM call (child of turn)</description></item>
    /// <item><description><b>agent.tool_call</b> — one per tool execution (child of iteration)</description></item>
    /// </list>
    ///
    /// The host application must configure an OTLP exporter to ship spans to a backend:
    /// <code>
    /// builder.Services.AddOpenTelemetry()
    ///     .WithTracing(t => t
    ///         .AddSource("HPD.Agent")
    ///         .AddOtlpExporter());
    /// </code>
    /// </summary>
    /// <param name="sourceName">ActivitySource name (default: "HPD.Agent").</param>
    /// <param name="sanitizerOptions">
    /// Controls redaction and length caps on span payloads (tool results, error messages).
    /// Defaults to 4KB cap and sensitive-field redaction enabled.
    /// </param>
    public AgentBuilder WithTracing(string? sourceName = null, SpanSanitizerOptions? sanitizerOptions = null)
    {
        _eventSubscriptionFactories.Add(coordinator =>
        {
            var tracing = new TracingObserver(sourceName ?? "HPD.Agent", sanitizerOptions);
            return new CompositeDisposable(
                coordinator.Subscribe<AgentEvent>(tracing.HandleAsync),
                tracing);
        });
        return this;
    }

    /// <summary>
    /// Enables comprehensive structured logging for observability:
    /// <list type="bullet">
    /// <item><description><b>LLM-level (Microsoft):</b> LLM invocation logging (requests/responses/errors)</description></item>
    /// <item><description><b>Agent-level (HPD):</b> Decision logging, state snapshots, circuit breaker warnings</description></item>
    /// <item><description><b>Unified Middleware:</b> Configurable logging at message turn, iteration, and function levels</description></item>
    /// </list>
    /// </summary>
    /// <param name="enableSensitiveData">Include prompts/responses at Trace level (default: false)</param>
    /// <param name="options">Optional logging middleware options. If null, uses default options (message turn + function logging).</param>
    /// <returns>The builder for chaining</returns>
    /// <remarks>
    /// <para>
    /// This method automatically registers multiple layers of logging:
    /// <list type="number">
    /// <item><description>Microsoft's <c>LoggingChatClient</c> middleware for LLM invocation logging</description></item>
    /// <item><description>HPD's <c>LoggingEventObserver</c> for agent orchestration logging</description></item>
    /// <item><description>Unified <c>LoggingMiddleware</c> for configurable agent lifecycle logging</description></item>
    /// </list>
    /// </para>
    /// <para><b>Requirements:</b> Call <c>WithServiceProvider()</c> with an <c>ILoggerFactory</c> registered.</para>
    /// <para><b>Log Levels:</b></para>
    /// <list type="bullet">
    /// <item><description><c>Debug</c> - LLM invocations, agent decisions, completions</description></item>
    /// <item><description><c>Information</c> - Agent completion summaries, middleware logging</description></item>
    /// <item><description><c>Warning</c> - Circuit breaker triggers, missing dependencies</description></item>
    /// <item><description><c>Trace</c> - Full message/response content (sensitive data)</description></item>
    /// <item><description><c>Error</c> - LLM errors, agent errors</description></item>
    /// </list>
    /// </remarks>
    /// <example>
    /// <code>
    /// // Default logging (message turns + functions)
    /// var agent = await new AgentBuilder()
    ///     .WithServiceProvider(services)
    ///     .WithLogging()
    ///     .WithOpenAI(model: "gpt-4", apiKey: apiKey)
    ///     .BuildAsync();
    ///
    /// // Minimal logging (just function names with timing)
    /// var agent = await new AgentBuilder()
    ///     .WithLogging(options: LoggingMiddlewareOptions.Minimal)
    ///     .BuildAsync();
    ///
    /// // Verbose logging (everything)
    /// var agent = await new AgentBuilder()
    ///     .WithLogging(options: LoggingMiddlewareOptions.Verbose)
    ///     .BuildAsync();
    ///
    /// // Custom configuration
    /// var agent = await new AgentBuilder()
    ///     .WithLogging(options: new LoggingMiddlewareOptions
    ///     {
    ///         LogFunction = true,
    ///         LogIteration = true,
    ///         IncludeArguments = false,
    ///         MaxStringLength = 500
    ///     })
    ///     .BuildAsync();
    /// </code>
    /// </example>
    public AgentBuilder WithLogging(
        bool enableSensitiveData = false,
        LoggingMiddlewareOptions? options = null)
    {
        // 1. Register Microsoft's LoggingChatClient middleware (user-facing LLM observability)
        // This provides LLM-level invocation logging (requests/responses)
        this.UseChatClientMiddleware((client, services) =>
        {
            var loggerFactory = services?.GetService<ILoggerFactory>();
            if (loggerFactory == null || loggerFactory == NullLoggerFactory.Instance)
            {
                // Log warning but don't fail - logging is optional
                _logger?.CreateLogger<AgentBuilder>().LogWarning(
                    "Logging is enabled but ILoggerFactory is not registered in service provider. LLM-level logging will be skipped.");
                return client;
            }

            var loggingClient = new LoggingChatClient(
                client,
                loggerFactory.CreateLogger(typeof(LoggingChatClient)));

            // Configure JSON serialization options to match HPD settings
            loggingClient.JsonSerializerOptions = AIJsonUtilities.DefaultOptions;

            return loggingClient;
        });

        // 2. Internally create LoggingEventObserver for agent-level observability (developer-only)
        // This tracks agent decisions, state, circuit breakers, etc.
        if (_logger != null)
        {
            var loggingObserver = new LoggingEventObserver(
                _logger.CreateLogger<LoggingEventObserver>(),
                enableSensitiveData);
            _eventSubscriptionFactories.Add(coordinator =>
                coordinator.Subscribe<AgentEvent>(loggingObserver.HandleAsync));
        }

        // 3. Store logging options - LoggingMiddleware will be added LAST in RegisterAutoMiddleware()
        // This ensures logging happens AFTER all other middleware (so it shows the final state)
        _loggingOptions = options ?? LoggingMiddlewareOptions.Default;

        return this;
    }

    /// <summary>
    /// Enables logging with an explicit logger factory.
    /// Use this when you want to configure logging without using dependency injection.
    /// </summary>
    /// <param name="loggerFactory">The logger factory to use for all logging.</param>
    /// <param name="options">Optional logging middleware options. If null, uses default options.</param>
    /// <returns>The builder for chaining</returns>
    /// <example>
    /// <code>
    /// var loggerFactory = LoggerFactory.Create(b => b.AddConsole());
    /// var agent = await new AgentBuilder()
    ///     .WithLogging(loggerFactory)
    ///     .WithProvider("openai", "gpt-4", apiKey)
    ///     .BuildAsync();
    /// </code>
    /// </example>
    public AgentBuilder WithLogging(
        ILoggerFactory loggerFactory,
        LoggingMiddlewareOptions? options = null)
    {
        _logger = loggerFactory;
        return WithLogging(enableSensitiveData: false, options: options);
    }

    /// <summary>
    /// Registers a subscription factory against the agent event coordinator during build.
    /// </summary>
    public AgentBuilder WithEventSubscription(
        Func<HPD.Events.IEventCoordinator, IDisposable> subscriptionFactory)
    {
        ArgumentNullException.ThrowIfNull(subscriptionFactory);
        _eventSubscriptionFactories.Add(subscriptionFactory);
        return this;
    }

    /// <summary>
    /// Enables distributed caching for LLM response caching.
    /// Dramatically reduces latency and cost for repeated queries.
    /// Automatically applies Microsoft's <c>DistributedCachingChatClient</c> middleware.
    /// </summary>
    /// <param name="cacheExpiration">Cache TTL (default: 30 minutes)</param>
    /// <param name="cacheStatefulConversations">Allow caching with ConversationId (default: false)</param>
    /// <returns>The builder for chaining</returns>
    /// <remarks>
    /// <para>
    /// This method automatically registers Microsoft's <c>DistributedCachingChatClient</c> middleware
    /// which caches LLM responses in <c>IDistributedCache</c> (Redis, Memory, SQL, etc.).
    /// </para>
    /// <para><b>Requirements:</b> Call <c>WithServiceProvider()</c> with an <c>IDistributedCache</c> registered.</para>
    /// <para><b>How It Works:</b></para>
    /// <list type="number">
    /// <item><description>Generates cache key from messages + options (uses JSON serialization)</description></item>
    /// <item><description>Checks cache before making LLM call (cache hit = skip LLM entirely)</description></item>
    /// <item><description>Stores LLM response in cache for future requests (coalesces streaming responses)</description></item>
    /// <item><description>Respects <paramref name="cacheExpiration"/> TTL</description></item>
    /// </list>
    /// <para><b>Performance Impact:</b></para>
    /// <list type="bullet">
    /// <item><description>Cache hit: ~1-5ms (vs 500-5000ms LLM call)</description></item>
    /// <item><description>Cost savings: 100% for cached responses (no LLM API call)</description></item>
    /// <item><description>Best for: Repeated queries, testing, demo environments</description></item>
    /// </list>
    /// </remarks>
    /// <example>
    /// <code>
    /// // Setup in your DI container:
    /// services.AddDistributedMemoryCache();  // Or Redis, SQL, etc.
    ///
    /// var agent = await new AgentBuilder()
    ///     .WithServiceProvider(services)  // IDistributedCache required
    ///     .WithCaching(
    ///         cacheExpiration: TimeSpan.FromHours(1),
    ///         cacheStatefulConversations: false)  // Don't cache with ConversationId
    ///     .WithOpenAI(model: "gpt-4", apiKey: apiKey)
    ///     .BuildAsync();
    ///
    /// // First call: Cache miss → LLM call → Store in cache
    /// await agent.RunAsync("What is 2+2?");
    ///
    /// // Second call: Cache hit → Return from cache (no LLM call!)
    /// await agent.RunAsync("What is 2+2?");
    /// </code>
    /// </example>
    public AgentBuilder WithCaching(TimeSpan? cacheExpiration = null, bool cacheStatefulConversations = false)
    {
        _config.Caching = new CachingConfig
        {
            Enabled = true,
            CacheExpiration = cacheExpiration ?? TimeSpan.FromMinutes(30),
            CacheStatefulConversations = cacheStatefulConversations,
            CoalesceStreamingUpdates = true
        };

        // Automatically register Microsoft's DistributedCachingChatClient middleware
        // This provides LLM-level response caching
        this.UseChatClientMiddleware((client, services) =>
        {
            var cache = services?.GetService<IDistributedCache>();
            if (cache == null)
            {
                // Log warning but don't fail - caching is optional
                _logger?.CreateLogger<AgentBuilder>().LogWarning(
                    "Caching is enabled but IDistributedCache is not registered in service provider. Caching will be skipped.");
                return client;
            }

            return new DistributedCachingChatClient(client, cache);
        });

        return this;
    }

    /// <summary>
    /// Enable background responses by default for all runs.
    /// When enabled, providers that support background mode can return immediately
    /// with a continuation token instead of blocking until completion.
    /// </summary>
    /// <param name="enabled">Whether to enable background responses by default.</param>
    /// <returns>The builder for chaining.</returns>
    /// <remarks>
    /// <para>
    /// Background responses help avoid HTTP gateway timeouts (e.g., AWS API Gateway 30s limit)
    /// by allowing the provider to start the operation and return a token for polling.
    /// </para>
    /// <para>
    /// This setting can be overridden per-request via <see cref="AgentRunConfig.AllowBackgroundResponses"/>.
    /// </para>
    /// </remarks>
    public AgentBuilder WithBackgroundResponses(bool enabled = true)
    {
        _config.BackgroundResponses ??= new BackgroundResponsesConfig();
        _config.BackgroundResponses.DefaultAllow = enabled;
        return this;
    }

    /// <summary>
    /// Configure background responses behavior with detailed options.
    /// </summary>
    /// <param name="configure">Action to configure background responses settings.</param>
    /// <returns>The builder for chaining.</returns>
    /// <example>
    /// <code>
    /// var agent = await new AgentBuilder()
    ///     .WithBackgroundResponses(config =>
    ///     {
    ///         config.DefaultAllow = true;
    ///         config.AutoPollToCompletion = true;
    ///         config.DefaultPollingInterval = TimeSpan.FromSeconds(3);
    ///         config.DefaultTimeout = TimeSpan.FromMinutes(10);
    ///     })
    ///     .BuildAsync();
    /// </code>
    /// </example>
    public AgentBuilder WithBackgroundResponses(Action<BackgroundResponsesConfig> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        _config.BackgroundResponses ??= new BackgroundResponsesConfig();
        configure(_config.BackgroundResponses);
        return this;
    }

    /// <summary>
    /// Configures a callback to transform ChatOptions before each LLM call.
    /// This allows dynamic runtime configuration without middleware complexity.
    /// </summary>
    /// <param name="configureOptions">Callback to modify ChatOptions before each request</param>
    /// <example>
    /// <code>
    /// builder.WithOptionsConfiguration(opts =>
    /// {
    ///     opts.Temperature = Math.Min(opts.Temperature ?? 1.0f, 0.8f);
    ///     opts.AdditionalProperties ??= new();
    ///     opts.AdditionalProperties["request_id"] = Guid.NewGuid().ToString();
    /// });
    /// </code>
    /// </example>
    public AgentBuilder WithOptionsConfiguration(Action<ChatOptions> configureOptions)
    {
        _config.ConfigureOptions = configureOptions ?? throw new ArgumentNullException(nameof(configureOptions));
        return this;
    }

    /// <summary>
    /// Sets the default reasoning options applied to every LLM call made by this agent.
    /// Can be overridden per-run via <see cref="AgentRunConfig.Chat"/>'s Reasoning property.
    /// </summary>
    /// <param name="effort">How much reasoning effort the model should apply.</param>
    /// <param name="output">Whether reasoning content is returned in the response.</param>
    /// <returns>The builder instance for chaining</returns>
    public AgentBuilder WithReasoning(ReasoningEffort effort = ReasoningEffort.Medium, ReasoningOutput output = ReasoningOutput.Full)
    {
        var chatConfig = _config.EnsureChatClientConfig();
        chatConfig.ChatDefaults ??= new ChatRunConfig();
        chatConfig.ChatDefaults.Reasoning = new ReasoningOptions
        {
            Effort = effort,
            Output = output
        };
        return this;
    }

    /// <summary>
    /// Includes reasoning/thinking content when projecting conversation history back to the model.
    /// Reasoning is always recorded in thread events when observed; this controls only whether
    /// reasoning blocks are included when sending history back to the provider,
    /// which is required for Anthropic extended thinking to work correctly across turns
    /// (ProtectedData must be round-tripped verbatim).
    /// Default: false (reasoning shown during streaming but excluded from history to save tokens).
    /// </summary>
    public AgentBuilder WithReasoningInModelHistory(bool include = true)
    {
        _config.IncludeReasoningInModelHistory = include;
        return this;
    }

    /// <summary>
    /// Adds middleware to wrap the IChatClient for custom processing.
    /// Middleware is applied dynamically on each request, so runtime provider switching still works.
    /// </summary>
    /// <param name="middleware">Function that wraps an IChatClient with custom behavior</param>
    /// <returns>The builder instance for chaining</returns>
    /// <remarks>
    /// <para>
    /// Unlike traditional middleware that wraps at build time, this middleware is applied
    /// on every request. This means runtime provider switching automatically applies your
    /// middleware to the new provider.
    /// </para>
    /// <para>
    /// Middleware is applied in the order added (first added = outermost wrapper).
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// builder
    ///     .UseChatClientMiddleware((client, services) =>
    ///         new RateLimitingChatClient(client, maxRequestsPerMinute: 60))
    ///     .UseChatClientMiddleware((client, services) =>
    ///         new CostTrackingChatClient(client, services?.GetService&lt;ICostTracker&gt;()));
    /// </code>
    /// </example>
    public AgentBuilder UseChatClientMiddleware(Func<IChatClient, IServiceProvider?, IChatClient> middleware)
    {
        ArgumentNullException.ThrowIfNull(middleware);
        _config.ClientMiddleware ??= new();
        _config.ClientMiddleware.Chat ??= new();
        _config.ClientMiddleware.Chat.Add(middleware);
        return this;
    }

    public AgentBuilder UseTextToSpeechClientMiddleware(Func<ITextToSpeechClient, IServiceProvider?, ITextToSpeechClient> middleware)
    {
        ArgumentNullException.ThrowIfNull(middleware);
        _config.ClientMiddleware ??= new();
        _config.ClientMiddleware.TextToSpeech ??= new();
        _config.ClientMiddleware.TextToSpeech.Add(middleware);
        return this;
    }

    public AgentBuilder UseSpeechToTextClientMiddleware(Func<ISpeechToTextClient, IServiceProvider?, ISpeechToTextClient> middleware)
    {
        ArgumentNullException.ThrowIfNull(middleware);
        _config.ClientMiddleware ??= new();
        _config.ClientMiddleware.SpeechToText ??= new();
        _config.ClientMiddleware.SpeechToText.Add(middleware);
        return this;
    }

    public AgentBuilder UseRealtimeClientMiddleware(Func<IRealtimeClient, IServiceProvider?, IRealtimeClient> middleware)
    {
        ArgumentNullException.ThrowIfNull(middleware);
        _config.ClientMiddleware ??= new();
        _config.ClientMiddleware.Realtime ??= new();
        _config.ClientMiddleware.Realtime.Add(middleware);
        return this;
    }

    public AgentBuilder UseImageGeneratorMiddleware(Func<IImageGenerator, IServiceProvider?, IImageGenerator> middleware)
    {
        ArgumentNullException.ThrowIfNull(middleware);
        _config.ClientMiddleware ??= new();
        _config.ClientMiddleware.ImageGeneration ??= new();
        _config.ClientMiddleware.ImageGeneration.Add(middleware);
        return this;
    }

    public AgentBuilder UseEmbeddingGeneratorMiddleware(Func<IEmbeddingGenerator, IServiceProvider?, IEmbeddingGenerator> middleware)
    {
        ArgumentNullException.ThrowIfNull(middleware);
        _config.ClientMiddleware ??= new();
        _config.ClientMiddleware.Embeddings ??= new();
        _config.ClientMiddleware.Embeddings.Add(middleware);
        return this;
    }

    public AgentBuilder UseHostedFileClientMiddleware(Func<IHostedFileClient, IServiceProvider?, IHostedFileClient> middleware)
    {
        ArgumentNullException.ThrowIfNull(middleware);
        _config.ClientMiddleware ??= new();
        _config.ClientMiddleware.HostedFiles ??= new();
        _config.ClientMiddleware.HostedFiles.Add(middleware);
        return this;
    }

    public AgentBuilder UseVoiceActivityDetectorMiddleware(
        Func<IVoiceActivityDetector, ProviderComponentLifetimeContext, IServiceProvider?, IVoiceActivityDetector> middleware)
    {
        ArgumentNullException.ThrowIfNull(middleware);
        _config.ClientMiddleware ??= new();
        _config.ClientMiddleware.VoiceActivityDetection ??= new();
        _config.ClientMiddleware.VoiceActivityDetection.Add(middleware);
        return this;
    }

    public AgentBuilder UseEndOfTurnDetectorMiddleware(
        Func<IEotDetector, ProviderComponentLifetimeContext, IServiceProvider?, IEotDetector> middleware)
    {
        ArgumentNullException.ThrowIfNull(middleware);
        _config.ClientMiddleware ??= new();
        _config.ClientMiddleware.EndOfTurnDetection ??= new();
        _config.ClientMiddleware.EndOfTurnDetection.Add(middleware);
        return this;
    }

    /// <summary>
    /// Sets an existing chat client to use instead of creating one from a provider.
    /// This is useful for SubAgents that want to inherit the parent's chat client.
    /// </summary>
    /// <param name="client">The chat client to use</param>
    /// <returns>The builder for method chaining</returns>
    /// <remarks>
    /// When this is set, the agent will use this client instead of creating a new one
    /// from the Provider configuration. The Provider configuration will still be validated
    /// but won't be used to create a client.
    /// </remarks>
    public AgentBuilder WithChatClient(IChatClient client)
    {
        ArgumentNullException.ThrowIfNull(client);
        _baseClient = client;
        return this;
    }

    /// <summary>
    /// Marks this agent as using a deferred provider - the chat client will be provided at runtime
    /// via AgentRunConfig.OverrideChatClient (typically inherited from a parent agent in workflows).
    /// This skips provider validation during Build() and allows building agents without configuring a provider.
    /// </summary>
    /// <remarks>
    /// Use this for agents that will run inside multi-agent workflows where the chat client
    /// is inherited from the parent agent at execution time.
    /// </remarks>
    public AgentBuilder WithDeferredProvider()
    {
        _deferredProvider = true;
        return this;
    }

    /// <summary>
    /// Sets the agent name
    /// </summary>
    public AgentBuilder WithName(string name)
    {
        _config.Name = name ?? throw new ArgumentNullException(nameof(name));
        return this;
    }

    /// <summary>
    /// Sets serializable default chat run options.
    /// </summary>
    public AgentBuilder WithChatDefaults(ChatRunConfig defaults)
    {
        ArgumentNullException.ThrowIfNull(defaults);
        EnsureChatClientConfig().ChatDefaults = defaults;
        return this;
    }

    /// <summary>
    /// Configures serializable default chat run options.
    /// </summary>
    public AgentBuilder WithChatDefaults(Action<ChatRunConfig> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        var chatConfig = EnsureChatClientConfig();
        chatConfig.ChatDefaults ??= new ChatRunConfig();
        configure(chatConfig.ChatDefaults);
        return this;
    }

    /// <summary>
    /// Sets runtime-only MEAI chat options for advanced in-process scenarios such as tools.
    /// Prefer WithChatDefaults for serializable defaults.
    /// </summary>
    public AgentBuilder WithDefaultMicrosoftChatOptions(ChatOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        EnsureChatClientConfig().SetDefaultMicrosoftChatOptions(options);
        return this;
    }

    /// <summary>
    /// Configures validation behavior for provider configuration during agent building.
    /// </summary>
    /// <param name="enableAsync">Whether to perform async validation (network calls)</param>
    public AgentBuilder WithValidation(bool enableAsync)
    {
        _config.Validation = new ValidationConfig
        {
            EnableAsyncValidation = enableAsync
        };
        return this;
    }





    /// <summary>
    /// Builds the protocol-agnostic core agent asynchronously.
    /// Required for provider validation (LLM connectivity checks) during initialization.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token for async operations</param>
    public async Task<Agent> BuildAsync(CancellationToken cancellationToken = default)
    {
        await ResolveStoredAgentDefinitionAsync(cancellationToken).ConfigureAwait(false);
        EnsureAutoConfiguration();

        // Build the secret resolver chain FIRST (before BuildDependenciesAsync)
        // Providers need ISecretResolver available in the service provider during CreateChatClient
        if (_secretResolver is null)
        {
            var resolvers = new List<ISecretResolver>();
            resolvers.Add(new EnvironmentSecretResolver());
            resolvers.AddRange(_additionalResolvers);
            if (_configuration != null)
                resolvers.Add(new ConfigurationSecretResolver(_configuration));
            _secretResolver = new ChainedSecretResolver(resolvers);
        }

        // Wrap the service provider to make ISecretResolver available to providers
        // This allows providers to resolve secrets during CreateChatClient without
        // replacing the user's service provider
        _serviceProvider = new CompositeServiceProvider(_serviceProvider, _secretResolver);

        var buildData = await BuildDependenciesAsync(cancellationToken).ConfigureAwait(false);

        // Default session store: InMemorySessionStore for zero-config out-of-the-box experience (V3)
        // Users can override with WithSessionStore() for persistent storage (FileSessionStore, etc.)
        if (_config.SessionStore == null)
        {
            _config.SessionStore = new InMemorySessionStore();
            _logger?.CreateLogger<AgentBuilder>().LogInformation(
                "Using default InMemorySessionStore (in-memory, ephemeral). " +
                "Use .WithSessionStore() for persistence.");
        }
        _config.SessionStoreOptions ??= new SessionStoreOptions();

        // Default content store: InMemoryContentStore for zero-config out-of-the-box experience (V3)
        // Users can override with WithContentStore() for persistent storage (LocalFileContentStore, etc.)
        if (_contentStore == null)
        {
            _contentStore = new InMemoryContentStore();
            _logger?.CreateLogger<AgentBuilder>().LogInformation(
                "Using default InMemoryContentStore (in-memory, ephemeral). " +
                "Use .WithContentStore() for persistence (e.g., LocalFileContentStore).");
        }

        // Resolve config middlewares before auto-middleware registration
        // This enables Config = Base, Builder = Override/Extend pattern
        ResolveConfigMiddlewares();

        ActivateRegisteredFeatures();
        RegisterAutoMiddleware(buildData);
        return CreateAgent(buildData);
    }

    private async Task ResolveStoredAgentDefinitionAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_config.AgentId))
            return;

        var agentId = _config.AgentId;
        var storeWasProvided = _config.AgentStore != null;
        var store = _config.AgentStore ??= AgentBuilderDefaults.AgentStore;

        var stored = await store.LoadAsync(agentId, cancellationToken).ConfigureAwait(false);
        if (stored?.Config != null)
        {
            MergeStoredConfigIntoCurrent(stored.Config);
        }

        _config.AgentId = agentId;
        _config.AgentStore = store;
        if (stored != null &&
            _config.Name == new AgentConfig().Name &&
            !string.IsNullOrWhiteSpace(stored.Name))
        {
            _config.Name = stored.Name;
        }

        if (_config.Name == new AgentConfig().Name)
            _config.Name = agentId;

        var shouldPersist =
            _config.AgentStoreOptions?.PersistOnBuild == true ||
            (!storeWasProvided && ReferenceEquals(store, AgentBuilderDefaults.AgentStore));

        if (!shouldPersist)
            return;

        var storedConfig = CloneSerializableConfig(_config);
        await store.SaveAsync(new StoredAgent
        {
            Id = agentId,
            Name = storedConfig.Name,
            Config = storedConfig,
            CreatedAt = stored?.CreatedAt ?? DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            Metadata = stored?.Metadata
        }, cancellationToken).ConfigureAwait(false);
    }

    private void MergeStoredConfigIntoCurrent(AgentConfig storedConfig)
    {
        var defaultConfig = new AgentConfig();
        var currentJson = SerializeConfigToObject(_config);
        var defaultJson = SerializeConfigToObject(defaultConfig);

        foreach (var property in typeof(AgentConfig).GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (!property.CanRead || !property.CanWrite)
                continue;

            if (property.GetCustomAttribute<JsonIgnoreAttribute>() != null)
                continue;

            var jsonName = GetJsonPropertyName(property);
            var currentIsDefault = IsJsonPropertyDefault(currentJson, defaultJson, jsonName);
            if (!currentIsDefault)
                continue;

            var storedValue = property.GetValue(storedConfig);
            if (storedValue != null)
                property.SetValue(_config, storedValue);
        }
    }

    private static AgentConfig CloneSerializableConfig(AgentConfig config)
    {
        var json = JsonSerializer.Serialize(config, HPDJsonContext.Default.AgentConfig);
        return JsonSerializer.Deserialize(json, HPDJsonContext.Default.AgentConfig)
            ?? throw new InvalidOperationException("Failed to clone AgentConfig for agent store persistence.");
    }

    private static JsonObject SerializeConfigToObject(AgentConfig config)
    {
        var json = JsonSerializer.Serialize(config, HPDJsonContext.Default.AgentConfig);
        return JsonNode.Parse(json)?.AsObject()
            ?? throw new InvalidOperationException("Failed to serialize AgentConfig for store merge.");
    }

    private static bool IsJsonPropertyDefault(JsonObject current, JsonObject defaultConfig, string jsonName)
    {
        var hasCurrent = current.TryGetPropertyValue(jsonName, out var currentValue);
        var hasDefault = defaultConfig.TryGetPropertyValue(jsonName, out var defaultValue);

        if (!hasCurrent && !hasDefault)
            return true;

        if (hasCurrent != hasDefault)
            return false;

        return JsonNode.DeepEquals(currentValue, defaultValue);
    }

    private static string GetJsonPropertyName(PropertyInfo property)
    {
        var attr = property.GetCustomAttribute<JsonPropertyNameAttribute>();
        if (attr != null)
            return attr.Name;

        var name = property.Name;
        return char.ToLowerInvariant(name[0]) + name[1..];
    }

    /// <summary>
    /// Registers all auto-middleware (error handling, compaction, tool Collapsing, etc).
    /// Called by both sync and async build paths to eliminate code duplication.
    /// </summary>
    private void RegisterAutoMiddleware(AgentBuildDependencies buildData)
    {
        // Set explicitly registered ToolHarnesses in config for Collapsing manager
        _config.explicitlyRegisteredToolHarnesses = _explicitlyRegisteredToolHarnesses
            .ToImmutableHashSet(StringComparer.OrdinalIgnoreCase);

        // Set global config for source-generated code to access (sync path sets this, harmless when called from async)
        AgentConfig.GlobalConfig = _config;

        // NOTE: ContinuationPermissionMiddleware is NO LONGER auto-registered.
        // To enable iteration limits with user permission requests, explicitly call:
        //   agentBuilder.WithMiddleware(new ContinuationPermissionMiddleware(maxIterations: 15))
        // This gives users full control over whether to ask for permission at iteration limits.

        // Register CompactionMiddleware if enabled
        // This reduces conversation history to manage context window size
        if (_config.Compaction?.Enabled == true)
        {
            var compactionStrategy = CreateCompactionStrategy(buildData.ClientToUse, _config, buildData.SummarizerClient);
            _middlewares.Add(new CompactionMiddleware
            {
                Strategy = compactionStrategy,
                Config = _config.Compaction,
                StrategyFactory = (options, runConfig) => CreateCompactionStrategy(
                    ResolveRunChatClient(runConfig) ?? buildData.ClientToUse,
                    _config,
                    CreateSummarizerClient(options as SummarizingCompactionOptions) ?? buildData.SummarizerClient,
                    options),
                SystemInstructions = _config.SystemInstructions
            });
        }

        // Register ContentUploadMiddleware for intelligent file upload routing.
        // Routes DataContent to HostedFileClient (provider-native) or IContentStore based on
        // provider capabilities and RunConfig.UploadStrategy (Auto/Hosted/Local).
        // _contentStore is guaranteed to be non-null due to auto-initialization in Build().
        _middlewares.Add(new Middleware.ContentUploadMiddleware(_providerRegistry, _contentStore));

        // Register ContentReferenceResolverMiddleware immediately after ContentUploadMiddleware.
        // Converts hpd-content:// URIs to provider-facing UriContent, HostedFileContent, or DataContent.
        // This ensures efficient message storage (URI refs) with transparent resolution.
        _middlewares.Add(new Middleware.ContentReferenceResolverMiddleware(_contentStore));

        // Register ImageMiddleware ALWAYS with default PassThrough strategy
        // Allows images to flow to vision models without processing
        // Users can override with .WithImageHandling() for custom strategies (OCR, Description, etc.)
        _middlewares.Add(new Middleware.Image.ImageMiddleware(
            new Middleware.Image.PassThroughImageStrategy()));

        //
        // AUTO-REGISTER FUNCTION-LEVEL MIDDLEWARE
        //
        // These are registered in execution order (first = outermost):
        // - FunctionRetryMiddleware wraps timeout (retry the entire timeout operation)
        // - FunctionTimeoutMiddleware wraps execution (timeout individual attempts)

        // Register FunctionRetryMiddleware if retry is enabled
        if (_config.ErrorHandling?.MaxRetries > 0)
        {
            _middlewares.Add(new Middleware.Function.RetryMiddleware(_config.ErrorHandling, buildData.ErrorHandler));
        }

        // Register FunctionTimeoutMiddleware if timeout is configured
        if (_config.ErrorHandling?.SingleFunctionTimeout != null)
        {
            _middlewares.Add(new Middleware.Function.FunctionTimeoutMiddleware(_config.ErrorHandling.SingleFunctionTimeout.Value));
        }

        // Register ErrorFormattingMiddleware ALWAYS (security boundary)
        // This sanitizes error messages to prevent exposing sensitive information to LLM
        // Even if ErrorHandling config is null, use default (secure) settings
        _middlewares.Add(new Middleware.Function.ErrorFormattingMiddleware(_config.ErrorHandling ?? new ErrorHandlingConfig(), buildData.ErrorHandler));

        // Register ContainerMiddleware if enabled
        // This unified middleware handles all container operations:
        // - Tool visibility filtering (collapsing)
        // -SystemPrompt injection
        // - Expansion detection
        // - Ephemeral result filtering
        if (_config.Collapsing?.Enabled == true && buildData.MergedOptions?.Tools != null)
        {
            var containerLogger = _logger?.CreateLogger<ContainerMiddleware>();
            var containerMiddleware = new ContainerMiddleware(
                buildData.MergedOptions.Tools,
                _explicitlyRegisteredToolHarnesses.ToImmutableHashSet(StringComparer.OrdinalIgnoreCase),
                _availableToolHarnesses,           // toolharness factory registry for scoped middleware
                _HARNESScopedMiddlewares,    // builder-time DI instances
                _toolharnessMiddlewareConfigs,    // config-ctor middleware configs from ToolHarnessReference
                _config.Collapsing,
                containerLogger);
            _middlewares.Add(containerMiddleware);

            // NOTE: ContainerErrorRecoveryMiddleware has been consolidated into ContainerMiddleware.
            // The Smart Recovery functionality (hidden items, qualified names) is now integrated
            // directly into ContainerMiddleware's BeforeToolExecutionAsync method.
        }

        // Register ClientToolMiddleware automatically
        // This enables Client-defined tools without explicit configuration.
        // It's a no-op if no Client ToolHarnesses are registered via AgentClientInput.
        // Users can override with WithClientTools() to customize config.
        if (!_middlewares.Any(m => m is ClientTools.ClientToolMiddleware))
        {
            _middlewares.Add(new ClientTools.ClientToolMiddleware());
        }

        // Register LoggingMiddleware LAST (if enabled via WithLogging())
        // This ensures it logs the FINAL state after all other middleware have run
        if (_loggingOptions != null)
        {
            var loggingMiddleware = new LoggingMiddleware(_logger, _loggingOptions);
            _middlewares.Add(loggingMiddleware);
        }
    }

    /// <summary>
    /// Creates the final Agent instance with all registered middleware and configuration.
    /// Shared by both sync and async build paths to eliminate code duplication.
    /// </summary>
    private Agent CreateAgent(AgentBuildDependencies buildData)
    {
        return new Agent(
            _config!,
            buildData.ClientToUse,
            buildData.MergedOptions,
            _functionToToolHarnessMap,
            _functionToSkillMap,
            _middlewares,
            _serviceProvider,
            _eventSubscriptionFactories,
            _providerRegistry,
            _contentStore,
            _stateFactories,
            buildData.OwnedHttpClients,
            buildData.ClientSet);
    }

    private void ActivateRegisteredFeatures()
    {
        foreach (var activate in AgentFeatureActivatorRegistry.Snapshot())
        {
            activate(this);
        }
    }

    /// <summary>
    /// Loads MCP tools from toolharness-owned [MCPServer] methods.
    /// Generated registration code collects MCP server sources directly; HPD-Agent.MCP owns concrete config handling.
    /// </summary>
    private async Task<List<AIFunction>> LoadToolHarnessMCPServersAsync(CancellationToken cancellationToken)
    {
        var allTools = new List<AIFunction>();

        var toolharnessesWithMcp = _selectedToolHarnessFactories
            .Where(f => f.HasMCPServers && f.CollectMcpServers != null)
            .ToList();
        if (toolharnessesWithMcp.Count == 0)
            return allTools;

        if (s_mcpToolLoader == null)
        {
            _logger?.CreateLogger<AgentBuilder>().LogWarning(
                "ToolHarnesses have [MCPServer] attributes but HPD-Agent.MCP is not loaded. Skipping MCP server loading.");
            return allTools;
        }

        if (McpClientManager == null)
        {
            var logger = _logger?.CreateLogger("HPD.Agent.MCP.MCPClientManager")
                ?? NullLogger.Instance;
            McpClientManager = s_mcpToolLoader.CreateManager(logger, _config.Mcp?.Options);
            _eventSubscriptionFactories.Add(coordinator =>
                s_mcpToolLoader.AttachLiveUpdates(McpClientManager!, coordinator));
        }

        var maxFunctionNames = _config.Collapsing?.MaxFunctionNamesInDescription ?? 10;

        foreach (var factory in toolharnessesWithMcp)
        {
            try
            {
                object? toolharnessInstance = null;
                if (_serviceProvider != null)
                {
                    toolharnessInstance = _serviceProvider.GetService(factory.ToolHarnessType);
                }
                if (toolharnessInstance == null && factory.CreateWithSecrets != null && _secretResolver != null)
                {
                    toolharnessInstance = factory.CreateWithSecrets(_secretResolver);
                }
                toolharnessInstance ??= factory.CreateInstance();

                var sources = new List<McpServerSource>();
                factory.CollectMcpServers!(toolharnessInstance, sources.Add);

                foreach (var source in sources)
                {
                    object? config = null;

                    if (source.FromManifest != null)
                    {
                        config = await s_mcpToolLoader.LoadConfigFromManifestAsync(
                            source.FromManifest,
                            source.ManifestServerName ?? source.Name,
                            cancellationToken).ConfigureAwait(false);
                    }
                    else if (source.ConfigProvider != null)
                    {
                        config = source.ConfigProvider(toolharnessInstance);
                    }

                    if (config == null)
                    {
                        _logger?.CreateLogger<AgentBuilder>().LogDebug(
                            "MCP server '{ServerName}' in toolharness '{ToolHarnessName}' returned null config, skipping",
                            source.Name, factory.Name);
                        continue;
                    }

                    var tools = await s_mcpToolLoader.LoadForToolHarnessAsync(
                        McpClientManager!,
                        config,
                        source,
                        _secretResolver,
                        maxFunctionNames,
                        cancellationToken).ConfigureAwait(false);
                    allTools.AddRange(tools);
                }
            }
            catch (Exception ex)
            {
                _logger?.CreateLogger<AgentBuilder>().LogWarning(ex,
                    "Failed to load MCP servers for toolharness '{ToolHarnessName}': {Error}",
                    factory.Name, ex.Message);
            }
        }

        return allTools;
    }

    private async Task<AgentToolBuildResult> BuildToolOptionsAsync(CancellationToken cancellationToken)
    {
        //
        // RESOLVE CONFIG HARNESS (Phase: Config Serialization)
        //
        // Resolve toolharnesses from config before creating functions.
        // This enables the Config = Base, Builder = Override/Extend pattern.
        ResolveConfigToolHarnesses();

        //
        // CREATE ToolHarness FUNCTIONS (AOT-Compatible - Zero Reflection in Hot Path)
        //
        // All ToolHarnesses are registered via the catalog (ToolRegistry.All) using direct delegate calls.
        // Instance-based ToolHarnesses (requiring DI) use their own direct delegate calls.
        // No reflection fallback - the catalog is required.
        var toolFunctions = CreateFunctionsFromCatalog();

        // Middleware out container functions if Collapsing is disabled.
        // Container functions are only needed when Collapsing is enabled for the two-turn expansion flow.
        if (_config.Collapsing?.Enabled != true)
        {
            toolFunctions = toolFunctions.Where(f =>
                !(f.AdditionalProperties?.TryGetValue("IsContainer", out var isContainer) == true &&
                  isContainer is bool isCont && isCont)
            ).ToList();
        }

        // Load MCP tools if configured.
        if (McpClientManager != null)
        {
            try
            {
                if (s_mcpToolLoader == null)
                    throw new InvalidOperationException(
                        "MCP client manager is configured but HPD-Agent.MCP loader is not registered. " +
                        "Reference HPD-Agent.MCP so its module initializer can register MCP support.");

                List<AIFunction> mcpTools;
                if (_config.Mcp != null &&
                    (!string.IsNullOrEmpty(_config.Mcp.ManifestPath) ||
                     !string.IsNullOrEmpty(_config.Mcp.ManifestContent)))
                {
                    var maxFunctionNames = _config.Collapsing?.MaxFunctionNamesInDescription ?? 10;

                    if (!string.IsNullOrEmpty(_config.Mcp.ManifestContent))
                    {
                        mcpTools = await s_mcpToolLoader.LoadFromManifestContentAsync(
                            McpClientManager,
                            _config.Mcp.ManifestContent,
                            _secretResolver,
                            maxFunctionNames,
                            cancellationToken).ConfigureAwait(false);
                    }
                    else
                    {
                        mcpTools = await s_mcpToolLoader.LoadFromManifestAsync(
                            McpClientManager,
                            _config.Mcp.ManifestPath,
                            _secretResolver,
                            maxFunctionNames,
                            cancellationToken).ConfigureAwait(false);
                    }
                }
                else
                {
                    throw new InvalidOperationException("MCP client manager is configured but no manifest path or content provided");
                }

                toolFunctions.AddRange(mcpTools);
                _logger?.CreateLogger<AgentBuilder>().LogInformation("Successfully integrated {Count} MCP tools into agent", mcpTools.Count);
            }
            catch (Exception ex)
            {
                _logger?.CreateLogger<AgentBuilder>().LogError(ex, "Failed to load MCP tools: {Error}", ex.Message);
                throw new InvalidOperationException("Failed to initialize MCP integration", ex);
            }
        }

        // Load toolharness-owned MCP servers (from [MCPServer] attributes).
        var toolharnessMcpTools = await LoadToolHarnessMCPServersAsync(cancellationToken);
        if (toolharnessMcpTools.Count > 0)
        {
            toolFunctions.AddRange(toolharnessMcpTools);
            _logger?.CreateLogger<AgentBuilder>().LogInformation("Successfully integrated {Count} toolharness-owned MCP tools into agent", toolharnessMcpTools.Count);
        }

        // Note: Old SkillDefinition-based skills have been removed in favor of type-safe Skill class.
        // Skills are now registered via ToolHarnesses and auto-discovered by the source generator.

        // Load OpenAPI sources (from WithOpenApi() or [OpenApi] toolharness attributes).
        OpenApiLoadResult? openApiResult = null;
        if (_openApiSources.Count > 0)
        {
            if (s_openApiLoader == null)
                throw new InvalidOperationException(
                    "OpenAPI sources were registered but HPD-Agent.OpenApi is not loaded. " +
                    "Add a reference to HPD-Agent.OpenApi.");

            openApiResult = await s_openApiLoader.LoadAllAsync(_openApiSources, cancellationToken);
            toolFunctions.AddRange(openApiResult.Functions);
            if (openApiResult.Functions.Count > 0)
                _logger?.CreateLogger<AgentBuilder>().LogInformation(
                    "Successfully integrated {Count} OpenAPI functions from {Sources} source(s)",
                    openApiResult.Functions.Count, _openApiSources.Count);
        }

        return new AgentToolBuildResult(
            MergeToolFunctions(_config.ResolveClientConfig(ProviderClientFamily.Chat)?.BuildEffectiveChatOptions(), toolFunctions),
            openApiResult?.OwnedHttpClients.Count > 0 ? openApiResult.OwnedHttpClients : null);
    }

    private IChatClient? CreateSummarizerClient(SummarizingCompactionOptions? overrideOptions = null)
    {
        var summarizingOptions = overrideOptions ?? _config.Compaction?.Strategy as SummarizingCompactionOptions;
        if (summarizingOptions?.SummarizerProvider == null)
            return null;

        var summarizerProviderKey = summarizingOptions.SummarizerProvider.ProviderKey;
        var summarizerProviderFeatures = _providerRegistry.GetRequiredProvider<IChatClientProvider>(summarizerProviderKey);

        return summarizerProviderFeatures.CreateChatClient(
            summarizingOptions.SummarizerProvider,
            _serviceProvider);
    }

    private IChatClient? ResolveRunChatClient(AgentRunConfig? runConfig)
    {
        if (runConfig?.OverrideChatClient is { } overrideClient)
            return overrideClient;

        var runClients = CreateRunClientOverrides(runConfig);
        if (runClients?.GetFamilyConfig(ProviderClientFamily.Chat) is null)
            return null;

        var effectiveConfig = _config.ResolveClientConfig(ProviderClientFamily.Chat, runClients);
        if (string.IsNullOrWhiteSpace(effectiveConfig?.ProviderKey))
            return null;

        var provider = _providerRegistry.GetRequiredProvider<IChatClientProvider>(effectiveConfig.ProviderKey);
        if (string.IsNullOrWhiteSpace(effectiveConfig.ModelName))
        {
            throw new InvalidOperationException(
                $"No model is configured for provider '{effectiveConfig.ProviderKey}'. Configure AgentConfig.Clients.Chat.ModelName or pass AgentRunConfig.ModelId.");
        }

        return provider.CreateChatClient(effectiveConfig, _serviceProvider);
    }

    private static AgentClientConfig? CreateRunClientOverrides(AgentRunConfig? options)
    {
        if (options is null)
            return null;

        if (options.Clients?.GetFamilyConfig(ProviderClientFamily.Chat) != null)
            return options.Clients;

        var chat = options.GetChatProviderOverride();
        if (chat is null)
            return options.Clients;

        return options.Clients is null
            ? new AgentClientConfig { Chat = chat }
            : new AgentClientConfig
            {
                Providers = options.Clients.Providers,
                Chat = chat,
                TextToSpeech = options.Clients.TextToSpeech,
                SpeechToText = options.Clients.SpeechToText,
                Realtime = options.Clients.Realtime,
                ImageGeneration = options.Clients.ImageGeneration,
                Embeddings = options.Clients.Embeddings,
                HostedFiles = options.Clients.HostedFiles,
                VoiceActivityDetection = options.Clients.VoiceActivityDetection,
                EndOfTurnDetection = options.Clients.EndOfTurnDetection
            };
    }

    private AgentClientSet CreateAgentClientSet(
        IChatClient? chat,
        IChatClient? summarizer,
        ClientProviderConfig? chatConfig)
    {
        var resolvedConfigs = new Dictionary<ProviderClientFamily, ClientProviderConfig>();
        if (chatConfig != null)
            resolvedConfigs[ProviderClientFamily.Chat] = ClientProviderConfigResolver.Clone(chatConfig);

        var textToSpeech = ResolveClientFamily<ITextToSpeechClientProvider, ITextToSpeechClient>(
            ProviderClientFamily.TextToSpeech,
            static (provider, config, services) => provider.CreateTextToSpeechClient(config, services),
            resolvedConfigs);

        var speechToText = ResolveClientFamily<ISpeechToTextClientProvider, ISpeechToTextClient>(
            ProviderClientFamily.SpeechToText,
            static (provider, config, services) => provider.CreateSpeechToTextClient(config, services),
            resolvedConfigs);

        var realtime = ResolveClientFamily<IRealtimeClientProvider, IRealtimeClient>(
            ProviderClientFamily.Realtime,
            static (provider, config, services) => provider.CreateRealtimeClient(config, services),
            resolvedConfigs);

        var imageGenerator = ResolveClientFamily<IImageGeneratorProvider, IImageGenerator>(
            ProviderClientFamily.ImageGeneration,
            static (provider, config, services) => provider.CreateImageGenerator(config, services),
            resolvedConfigs);

        var embeddingGenerator = ResolveClientFamily<IEmbeddingGeneratorProvider, IEmbeddingGenerator>(
            ProviderClientFamily.Embeddings,
            static (provider, config, services) => provider.CreateEmbeddingGenerator(config, services),
            resolvedConfigs);

        var hostedFiles = ResolveClientFamily<IHostedFileClientProvider, IHostedFileClient>(
            ProviderClientFamily.HostedFiles,
            static (provider, config, services) => provider.CreateHostedFileClient(config, services),
            resolvedConfigs);

        var vadFactory = ResolveComponentFactory<IVoiceActivityDetectorProvider, IVoiceActivityDetector>(
            ProviderClientFamily.VoiceActivityDetection,
            ProviderFamilyLifetime.StatefulPerAudioSession,
            static (provider, config, context, services) =>
                provider.CreateVoiceActivityDetector(config, context, services),
            resolvedConfigs);

        var eotFactory = ResolveComponentFactory<IEndOfTurnDetectorProvider, IEotDetector>(
            ProviderClientFamily.EndOfTurnDetection,
            ProviderFamilyLifetime.StatefulPerAudioSession,
            static (provider, config, context, services) =>
                provider.CreateEndOfTurnDetector(config, context, services),
            resolvedConfigs);

        return new AgentClientSet
        {
            Chat = chat,
            Summarizer = summarizer,
            TextToSpeech = textToSpeech,
            SpeechToText = speechToText,
            Realtime = realtime,
            ImageGenerator = imageGenerator,
            EmbeddingGenerator = embeddingGenerator,
            HostedFiles = hostedFiles,
            VoiceActivityDetectorFactory = vadFactory,
            EndOfTurnDetectorFactory = eotFactory,
            ResolvedConfigs = resolvedConfigs
        };
    }

    private TClient? ResolveClientFamily<TProvider, TClient>(
        ProviderClientFamily family,
        Func<TProvider, ClientProviderConfig, IServiceProvider?, TClient> createClient,
        Dictionary<ProviderClientFamily, ClientProviderConfig> resolvedConfigs)
        where TProvider : class, IProvider
    {
        var config = _config.ResolveClientConfig(family);
        if (config == null || string.IsNullOrWhiteSpace(config.ProviderKey))
            return default;

        var provider = _providerRegistry.GetRequiredProvider<TProvider>(config.ProviderKey);
        resolvedConfigs[family] = ClientProviderConfigResolver.Clone(config);
        return ApplyClientMiddleware(family, createClient(provider, config, _serviceProvider));
    }

    private Func<ProviderComponentLifetimeContext, TComponent>? ResolveComponentFactory<TProvider, TComponent>(
        ProviderClientFamily family,
        ProviderFamilyLifetime defaultLifetime,
        Func<TProvider, ClientProviderConfig, ProviderComponentLifetimeContext, IServiceProvider?, TComponent> createComponent,
        Dictionary<ProviderClientFamily, ClientProviderConfig> resolvedConfigs)
        where TProvider : class, IProvider
    {
        var config = _config.ResolveClientConfig(family);
        if (config == null || string.IsNullOrWhiteSpace(config.ProviderKey))
            return null;

        var provider = _providerRegistry.GetRequiredProvider<TProvider>(config.ProviderKey);
        var capturedConfig = ClientProviderConfigResolver.Clone(config);
        resolvedConfigs[family] = capturedConfig;

        var lifetime = provider.GetMetadata().Families.TryGetValue(family, out var descriptor)
            ? descriptor.Lifetime
            : defaultLifetime;

        return context =>
        {
            var scopedContext = context.Lifetime == ProviderFamilyLifetime.ReusableClient
                ? context with { Lifetime = lifetime }
                : context;

            return ApplyComponentMiddleware(
                family,
                createComponent(provider, capturedConfig, scopedContext, _serviceProvider),
                scopedContext);
        };
    }

    private TClient ApplyClientMiddleware<TClient>(ProviderClientFamily family, TClient client)
    {
        return family switch
        {
            ProviderClientFamily.Chat when client is IChatClient chat =>
                (TClient)(object)ApplyMiddleware(chat, _config.ClientMiddleware?.Chat, "chat client"),
            ProviderClientFamily.TextToSpeech when client is ITextToSpeechClient tts =>
                (TClient)(object)ApplyMiddleware(tts, _config.ClientMiddleware?.TextToSpeech, "text-to-speech client"),
            ProviderClientFamily.SpeechToText when client is ISpeechToTextClient stt =>
                (TClient)(object)ApplyMiddleware(stt, _config.ClientMiddleware?.SpeechToText, "speech-to-text client"),
            ProviderClientFamily.Realtime when client is IRealtimeClient realtime =>
                (TClient)(object)ApplyMiddleware(realtime, _config.ClientMiddleware?.Realtime, "realtime client"),
            ProviderClientFamily.ImageGeneration when client is IImageGenerator imageGenerator =>
                (TClient)(object)ApplyMiddleware(imageGenerator, _config.ClientMiddleware?.ImageGeneration, "image generator"),
            ProviderClientFamily.Embeddings when client is IEmbeddingGenerator embeddingGenerator =>
                (TClient)(object)ApplyMiddleware(embeddingGenerator, _config.ClientMiddleware?.Embeddings, "embedding generator"),
            ProviderClientFamily.HostedFiles when client is IHostedFileClient hostedFiles =>
                (TClient)(object)ApplyMiddleware(hostedFiles, _config.ClientMiddleware?.HostedFiles, "hosted file client"),
            _ => client
        };
    }

    private TComponent ApplyComponentMiddleware<TComponent>(
        ProviderClientFamily family,
        TComponent component,
        ProviderComponentLifetimeContext context)
    {
        return family switch
        {
            ProviderClientFamily.VoiceActivityDetection when component is IVoiceActivityDetector vad =>
                (TComponent)(object)ApplyMiddleware(vad, _config.ClientMiddleware?.VoiceActivityDetection, context, "voice activity detector"),
            ProviderClientFamily.EndOfTurnDetection when component is IEotDetector eot =>
                (TComponent)(object)ApplyMiddleware(eot, _config.ClientMiddleware?.EndOfTurnDetection, context, "end-of-turn detector"),
            _ => component
        };
    }

    private TClient ApplyMiddleware<TClient>(
        TClient client,
        IReadOnlyList<Func<TClient, IServiceProvider?, TClient>>? middleware,
        string clientDescription)
    {
        if (middleware == null || middleware.Count == 0)
            return client;

        var effective = client;
        for (var i = middleware.Count - 1; i >= 0; i--)
        {
            effective = middleware[i](effective, _serviceProvider)
                ?? throw new InvalidOperationException($"{clientDescription} middleware returned null.");
        }

        return effective;
    }

    private TComponent ApplyMiddleware<TComponent>(
        TComponent component,
        IReadOnlyList<Func<TComponent, ProviderComponentLifetimeContext, IServiceProvider?, TComponent>>? middleware,
        ProviderComponentLifetimeContext context,
        string componentDescription)
    {
        if (middleware == null || middleware.Count == 0)
            return component;

        var effective = component;
        for (var i = middleware.Count - 1; i >= 0; i--)
        {
            effective = middleware[i](effective, context, _serviceProvider)
                ?? throw new InvalidOperationException($"{componentDescription} middleware returned null.");
        }

        return effective;
    }

    /// <summary>
    /// Builds all dependencies needed for agent construction.
    /// </summary>
    private async Task<AgentBuildDependencies> BuildDependenciesAsync(CancellationToken cancellationToken)
    {
        // === TESTING BYPASS: If BaseClient is already set, skip provider resolution ===
        // This allows tests to inject fake clients without configuring a real provider
        if (_baseClient != null)
        {
            // Use generic error handler for testing
            var testErrorHandler = new HPD.Agent.ErrorHandling.GenericErrorHandler();
            var toolBuild = await BuildToolOptionsAsync(cancellationToken).ConfigureAwait(false);
            var injectedSummarizerClient = CreateSummarizerClient();

            return new AgentBuildDependencies(
                CreateAgentClientSet(_baseClient, injectedSummarizerClient, chatConfig: null),
                toolBuild.MergedOptions,
                testErrorHandler,
                OwnedHttpClients: toolBuild.OwnedHttpClients);
        }

        // === START: VALIDATION LOGIC ===
        AgentConfigValidator.ValidateAndThrow(_config);
        var chatProviderConfig = _config.ResolveClientConfig(ProviderClientFamily.Chat);

        // === RUNTIME PROVIDER: Skip provider client creation when a chat client
        // will be selected at invocation time via AgentRunConfig.
        if (_deferredProvider || chatProviderConfig == null)
        {
            var runtimeErrorHandler = new HPD.Agent.ErrorHandling.GenericErrorHandler();
            var toolBuild = await BuildToolOptionsAsync(cancellationToken).ConfigureAwait(false);
            var runtimeSummarizerClient = CreateSummarizerClient();
            return new AgentBuildDependencies(
                CreateAgentClientSet(null, runtimeSummarizerClient, chatConfig: null),
                toolBuild.MergedOptions,
                runtimeErrorHandler,
                OwnedHttpClients: toolBuild.OwnedHttpClients);
        }

        EnsureAutoConfiguration();

        // Fill missing values from the matching Providers section.
        if (_configuration != null && !string.IsNullOrEmpty(chatProviderConfig.ProviderKey))
        {
            var providerName = chatProviderConfig.ProviderKey;

            if (TryGetExactConfigurationValue(_configuration, $"Providers:{providerName}:ProviderKey", out var sectionProviderKey) &&
                string.IsNullOrEmpty(chatProviderConfig.ProviderKey))
                chatProviderConfig.ProviderKey = sectionProviderKey;

            if (TryGetExactConfigurationValue(_configuration, $"Providers:{providerName}:ApiKey", out var sectionApiKey) &&
                string.IsNullOrEmpty(chatProviderConfig.ApiKey))
                chatProviderConfig.ApiKey = sectionApiKey;

            if (TryGetExactConfigurationValue(_configuration, $"Providers:{providerName}:ModelName", out var sectionModelName) &&
                string.IsNullOrEmpty(chatProviderConfig.ModelName))
                chatProviderConfig.ModelName = sectionModelName;

            if (TryGetExactConfigurationValue(_configuration, $"Providers:{providerName}:Endpoint", out var sectionEndpoint) &&
                string.IsNullOrEmpty(chatProviderConfig.Endpoint))
                chatProviderConfig.Endpoint = sectionEndpoint;
        }

        // Resolve provider from registry
        var providerKey = chatProviderConfig.ProviderKey;
        if (string.IsNullOrEmpty(providerKey) || string.IsNullOrEmpty(chatProviderConfig.ModelName))
        {
            var runtimeErrorHandler = new HPD.Agent.ErrorHandling.GenericErrorHandler();
            var toolBuild = await BuildToolOptionsAsync(cancellationToken).ConfigureAwait(false);
            var fallbackSummarizerClient = CreateSummarizerClient();
            return new AgentBuildDependencies(
                CreateAgentClientSet(null, fallbackSummarizerClient, chatConfig: null),
                toolBuild.MergedOptions,
                runtimeErrorHandler,
                OwnedHttpClients: toolBuild.OwnedHttpClients);
        }

        var providerFeatures = _providerRegistry.GetRequiredProvider<IChatClientProvider>(providerKey);

        // Validate provider-specific configuration
        ProviderValidationResult validation;

        // Check if async validation is enabled in configuration
        var enableAsyncValidation = _config.Validation?.EnableAsyncValidation ?? false;

        // Try async validation first if enabled and supported
        if (enableAsyncValidation)
        {
            var asyncValidationTask = providerFeatures.ValidateConfigurationAsync(
                chatProviderConfig,
                ProviderClientFamily.Chat,
                cancellationToken);

            // If provider supports async validation (returns non-null Task)
            if (asyncValidationTask != null)
            {
                var asyncValidation = await asyncValidationTask.ConfigureAwait(false);

                // If async validation returned a result, use it; otherwise fall back to sync
                if (asyncValidation != null)
                {
                    validation = asyncValidation;
                    _logger?.CreateLogger<AgentBuilder>().LogDebug(
                        "Used async validation for provider '{ProviderKey}'", providerKey);
                }
                else
                {
                    // Async task completed but returned null, use sync
                    validation = providerFeatures.ValidateConfiguration(chatProviderConfig, ProviderClientFamily.Chat);
                }
            }
            else
            {
                // Provider doesn't support async validation (returns null Task), use sync
                validation = providerFeatures.ValidateConfiguration(chatProviderConfig, ProviderClientFamily.Chat);
            }
        }
        else
        {
            // Async validation disabled, use sync only
            validation = providerFeatures.ValidateConfiguration(chatProviderConfig, ProviderClientFamily.Chat);
        }

        if (!validation.IsValid)
        {
            // Check if this is an API key issue and provide helpful guidance
            var hasApiKeyError = validation.Errors.Any(e =>
                e.Contains("API key", StringComparison.OrdinalIgnoreCase) ||
                e.Contains("ApiKey", StringComparison.OrdinalIgnoreCase) ||
                e.Contains("AccessKey", StringComparison.OrdinalIgnoreCase));

                var errorMessage = $"Provider configuration for '{providerKey}' is invalid:\n- {string.Join("\n- ", validation.Errors)}";

            if (hasApiKeyError)
            {
                var providerUpper = providerKey.ToUpperInvariant();

                errorMessage += $"\n\nConfigure your API key using the provider section or typed agent config:\n\n" +
                    $" PROVIDERS SECTION:\n" +
                    $"   appsettings.json → \"Providers\": {{\n" +
                    $"     \"{providerKey}\": {{\n" +
                    $"       \"ProviderKey\": \"{providerKey}\",\n" +
                    $"       \"ModelName\": \"your-model\",\n" +
                    $"       \"ApiKey\": \"your-api-key\"\n" +
                    $"     }}\n" +
                    $"   }}\n\n" +
                    $"  ENVIRONMENT VARIABLE:\n" +
                    $"   {providerUpper}_API_KEY=your-api-key\n\n" +
                    $" USER SECRETS (development only):\n" +
                    $"   dotnet user-secrets set \"Providers:{providerKey}:ApiKey\" \"your-api-key\"\n\n" +
                    $" CODE (for testing only, not recommended):\n" +
                    $"   Clients = new AgentClientConfig {{ Chat = new ClientProviderConfig {{ ApiKey = \"your-api-key\", ... }} }}";
            }            throw new InvalidOperationException(errorMessage);
        }

        // Create chat client and error handler via provider factories
        // Skip client creation if WithChatClient() was used (e.g., SubAgent inheriting parent's client)
        if (_baseClient == null)
        {
            _baseClient = providerFeatures.CreateChatClient(chatProviderConfig, _serviceProvider);

            if (_baseClient == null)
                throw new InvalidOperationException($"The factory for provider '{providerKey}' returned a null chat client.");
        }

        // Note: Error handler is now created in the middleware registration phase above,
        // not here. This ensures it's only created if retry is actually enabled.

        // Use base client directly (no middleware pipeline)
        // Observability (telemetry, logging, caching) is integrated directly into Agent.cs
        var clientToUse = _baseClient;

        // Dynamic Memory registration is handled by WithDynamicMemory() extension method
        // No need to register here in Build() - the extension already adds Middleware and ToolHarness

        var builtTools = await BuildToolOptionsAsync(cancellationToken).ConfigureAwait(false);

        var summarizerClient = CreateSummarizerClient();

        // Create the provider-specific error handler
        var errorHandler = providerFeatures.CreateErrorHandler();
        if (errorHandler == null)
            throw new InvalidOperationException($"The factory for provider '{providerKey}' returned a null error handler.");

        // Return dependencies instead of creating agent
        return new AgentBuildDependencies(
            CreateAgentClientSet(clientToUse, summarizerClient, chatProviderConfig),
            builtTools.MergedOptions,
            errorHandler,
            builtTools.OwnedHttpClients);
    }

    private void EnsureAutoConfiguration()
    {
        if (_configuration != null)
            return;

        try
        {
            // Load from the output directory for normal app execution and from the
            // current directory for `dotnet run` from a project folder.
            var basePath = AppContext.BaseDirectory ?? Directory.GetCurrentDirectory();
            var currentPath = Directory.GetCurrentDirectory();

            var configuration = new ConfigurationBuilder()
                .SetBasePath(basePath)
                .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true);

            if (!string.Equals(basePath, currentPath, StringComparison.Ordinal))
            {
                configuration.AddJsonFile(
                    Path.Combine(currentPath, "appsettings.json"),
                    optional: true,
                    reloadOnChange: true);
            }

            if (Assembly.GetEntryAssembly() is { } entryAssembly)
            {
                configuration.AddUserSecrets(entryAssembly, optional: true, reloadOnChange: true);
            }

            _configuration = configuration
                .AddEnvironmentVariables()
                .Build();
        }
        catch (Exception ex)
        {
            // If auto-configuration fails, we'll continue without it
            // and provide helpful error message later if API key is missing.
            Console.WriteLine($"[AgentBuilder] Auto-configuration warning: {ex.Message}");
        }
    }

    public bool IsProviderRegistered(string providerKey) => _providerRegistry.IsRegistered(providerKey);

    public IReadOnlyCollection<string> GetAvailableProviders() => _providerRegistry.GetRegisteredProviders();

    private static bool TryGetExactConfigurationValue(IConfiguration configuration, string key, out string value)
    {
        foreach (var pair in configuration.AsEnumerable())
        {
            if (string.Equals(pair.Key, key, StringComparison.Ordinal) &&
                !string.IsNullOrWhiteSpace(pair.Value))
            {
                value = pair.Value;
                return true;
            }
        }

        value = string.Empty;
        return false;
    }

    private ClientProviderConfig EnsureChatClientConfig()
    {
        return _config.EnsureChatClientConfig();
    }

    /// <summary>
    /// Merges ToolHarness functions into chat options.
    /// </summary>
    private ChatOptions? MergeToolFunctions(ChatOptions? defaultOptions, List<AIFunction> toolFunctions)
    {
        if (toolFunctions.Count == 0)
            return defaultOptions;

        var options = defaultOptions?.Clone() ?? new ChatOptions();

        // Add ToolHarness functions to existing tools
        var allTools = new List<AITool>(options.Tools ?? []);
        allTools.AddRange(toolFunctions);

        // Translate ToolSelectionConfig to ChatToolMode (FFI-friendly → M.E.AI)
        var toolMode = TranslateToolMode(_config.ToolSelection);

        options.Tools = allTools;
        options.ToolMode = toolMode;
        return options;
    }

    /// <summary>
    /// Translates FFI-friendly ToolSelectionConfig to Microsoft.Extensions.AI ChatToolMode.
    /// This keeps foreign language bindings (Python, JS, etc.) free from M.E.AI dependencies.
    /// </summary>
    private static ChatToolMode TranslateToolMode(ToolSelectionConfig? toolSelection)
    {
        if (toolSelection == null)
            return ChatToolMode.Auto;

        return toolSelection.ToolMode switch
        {
            "None" => ChatToolMode.None,
            "RequireAny" => ChatToolMode.RequireAny,
            "RequireSpecific" when !string.IsNullOrEmpty(toolSelection.RequiredFunctionName)
                => ChatToolMode.RequireSpecific(toolSelection.RequiredFunctionName),
            "RequireSpecific"
                => throw new InvalidOperationException("ToolMode 'RequireSpecific' requires RequiredFunctionName to be set."),
            "Auto" => ChatToolMode.Auto,
            _ => throw new InvalidOperationException($"Unknown ToolMode: '{toolSelection.ToolMode}'. Valid values: 'Auto', 'None', 'RequireAny', 'RequireSpecific'.")
        };
    }


    /// <summary>
    /// Creates a new builder instance
    /// </summary>
    [System.Diagnostics.CodeAnalysis.RequiresUnreferencedCode("Provider assembly loading uses reflection in non-AOT scenarios")]
    public static AgentBuilder Create() => new();


    // Public properties for extension methods
    /// <summary>
    /// Gets the agent name for use in extension methods
    /// </summary>
    public string AgentName => _config.Name;

    /// <summary>
    /// Gets the configuration instance for this builder (if provided).
    /// Used by extension methods and ToolHarnesses to access configuration values.
    /// </summary>
    public IConfiguration? Configuration => _configuration;

    /// <summary>
    /// Gets the configuration object for this builder.
    /// Used by provider extension methods to configure provider-specific settings.
    /// </summary>
    public AgentConfig Config => _config;

    /// <summary>
    /// Gets the provider registry used by this builder.
    /// Extension packages use this to resolve provider-backed clients through the
    /// same registry as the core agent.
    /// </summary>
    public IProviderRegistry ProviderRegistry => _providerRegistry;

    /// <summary>
    /// Internal access to base client for extension methods
    /// </summary>
    internal IChatClient? BaseClient
    {
        get => _baseClient;
        set => _baseClient = value;
    }


    /// <summary>
    /// Internal access to provider configs for extension methods
    /// </summary>
    internal Dictionary<Type, object> ProviderConfigs => _providerConfigs;

    /// <summary>
    /// Internal access to service provider for extension methods
    /// </summary>
    public IServiceProvider? ServiceProvider => _serviceProvider;

    /// <summary>
    /// Gets the logger factory for use in extension methods.
    /// Used by MCP and other extension methods to create loggers.
    /// </summary>
    public ILoggerFactory? Logger => _logger;

    /// <summary>
    /// Internal access to Collapsed Middleware manager for extension methods
    /// </summary>

    /// <summary>
    /// Internal access to default ToolHarness context for extension methods
    /// </summary>
    internal IToolMetadata? DefaulTMetadata
    {
        get => _defaulTMetadata;
        set => _defaulTMetadata = value;
    }

    /// <summary>
    /// Public access to ToolHarness contexts for extension methods and external configuration
    /// </summary>
    public Dictionary<string, IToolMetadata?> ToolHarnessContexts => _toolharnessContexts;

    /// <summary>
    /// Configures serializer options used to marshal tool return values into event-safe JSON payloads.
    /// </summary>
    public AgentBuilder WithToolSerializerOptions(JsonSerializerOptions serializerOptions)
    {
        ArgumentNullException.ThrowIfNull(serializerOptions);

        var options = new JsonSerializerOptions(serializerOptions);
        if (AIJsonUtilities.DefaultOptions.TypeInfoResolver is { } aiResolver)
            options.TypeInfoResolverChain.Add(aiResolver);
        options.TypeInfoResolverChain.Add(HPDJsonContext.Default);
        options.MakeReadOnly();
        _toolSerializerOptions = options;
        return this;
    }

    /// <summary>
    /// Public access to unified middlewares for extension methods and external configuration
    /// </summary>
    public List<Middleware.IAgentMiddleware> Middlewares => _middlewares;

    /// <summary>
    /// Internal access to permission Middlewares for extension methods
    /// </summary>

    /// <summary>
    /// Gets or sets the MCP client manager for extension methods (stored as object to avoid circular reference).
    /// Used by MCP extension methods to initialize and manage MCP server connections.
    /// </summary>
    public object? McpClientManager
    {
        get => _mcpClientManager;
        set => _mcpClientManager = value;
    }

    /// <summary>
    /// Adds a native function to the agent (used by FFI layer for Rust, C++, etc.)
    /// This method is intended primarily for FFI integration with native ToolHarnesses.
    /// </summary>
    public AgentBuilder WithNativeFunction(AIFunction function)
    {
        // Get or create default chat options
        var chatConfig = EnsureChatClientConfig();
        if (chatConfig.DefaultMicrosoftChatOptions == null)
            chatConfig.DefaultMicrosoftChatOptions = new ChatOptions();

        // Add to tools list
        var tools = chatConfig.DefaultMicrosoftChatOptions.Tools?.ToList() ?? new List<AITool>();
        tools.Add(function);
        chatConfig.DefaultMicrosoftChatOptions.Tools = tools;

        // Enable auto tool mode if not already set
        if (chatConfig.DefaultMicrosoftChatOptions.ToolMode == null)
            chatConfig.DefaultMicrosoftChatOptions.ToolMode = ChatToolMode.Auto;

        return this;
    }

    /// <summary>
    /// Creates a normalized HPD compaction strategy based on the configured strategy options.
    /// </summary>
    private static ICompactionStrategy? CreateCompactionStrategy(
        IChatClient? baseClient,
        AgentConfig config,
        IChatClient? summarizerClient,
        CompactionStrategyOptions? overrideStrategy = null)
    {
        var historyConfig = config.Compaction;

        if (historyConfig == null || !historyConfig.Enabled)
        {
            return null;
        }

        var strategy = overrideStrategy ?? historyConfig.Strategy;

        Func<IReadOnlyList<ChatMessage>, IChatReducer> reducerFactory = strategy switch
        {
            MessageCountingCompactionOptions messageCounting =>
                messages => new MessageCountingChatReducer(
                    ResolvePreserveRecentRawMessageCount(messages, messageCounting)),

            SummarizingCompactionOptions summarizing =>
                messages => CreateSummarizingReducer(
                    baseClient,
                    summarizing,
                    summarizerClient,
                    ResolvePreserveRecentRawMessageCount(messages, summarizing)),

            _ => throw new ArgumentException($"Unknown compaction strategy: {strategy.GetType().Name}")
        };

        return new ChatReducerCompactionStrategy(reducerFactory, strategy);
    }

    /// <summary>
    /// Creates a SummarizingChatReducer with custom configuration.
    /// Supports using a separate, cheaper model for summarization (cost optimization).
    /// </summary>
    private static SummarizingChatReducer CreateSummarizingReducer(
        IChatClient? baseClient,
        SummarizingCompactionOptions options,
        IChatClient? summarizerClient,
        int preserveRecentRawMessageCount)
    {
        // Determine which chat client to use for summarization
        // If a custom summarizer client was provided, use it
        // Otherwise, fall back to the base client
        var clientForSummarization = summarizerClient ?? baseClient;
        if (clientForSummarization == null)
        {
            throw new InvalidOperationException(
                "History compaction with summarization requires a configured provider or summarizer provider.");
        }

        // SummarizingChatReducer requires a target greater than zero. When HPD's
        // policy preserves zero raw messages, ChatReducerCompactionStrategy adds
        // and later removes a private sentinel that occupies this one slot.
        var reducer = new SummarizingChatReducer(
            clientForSummarization,
            Math.Max(1, preserveRecentRawMessageCount),
            options.ResummarizeAfterNewMessages);

        if (!string.IsNullOrEmpty(options.CustomPrompt))
        {
            reducer.SummarizationPrompt = options.CustomPrompt;
        }
        else if (options.SummaryStyle == SummaryStyle.Handoff)
        {
            reducer.SummarizationPrompt = CreateHandoffSummarizationPrompt(options.UseSingleSummary);
        }

        return reducer;
    }

    internal static int ResolvePreserveRecentRawMessageCount(
        IReadOnlyList<ChatMessage> messages,
        CompactionStrategyOptions strategy)
    {
        ArgumentNullException.ThrowIfNull(strategy);

        if (messages.Count == 0)
            return 0;

        if (!string.IsNullOrWhiteSpace(strategy.PreserveFromMessageTurnId) ||
            !string.IsNullOrWhiteSpace(strategy.PreserveFromMessageId))
        {
            return ResolvePreserveFromBoundaryRawMessageCount(
                messages,
                strategy.PreserveFromMessageId,
                strategy.PreserveFromMessageTurnId);
        }

        return strategy switch
        {
            MessageCountingCompactionOptions messageCounting =>
                ResolvePreserveRecentRawMessageCount(messages, messageCounting.PreserveRecentUserTurnCount),
            SummarizingCompactionOptions summarizing =>
                ResolvePreserveRecentRawMessageCount(messages, summarizing.PreserveRecentUserTurnCount),
            _ => messages.Count
        };
    }

    internal static int ResolvePreserveRecentRawMessageCount(
        IReadOnlyList<ChatMessage> messages,
        int preserveRecentUserTurnCount)
    {
        if (messages.Count == 0)
            return 0;

        if (preserveRecentUserTurnCount <= 0)
            return 0;

        var seenUserTurnIds = new HashSet<string>(StringComparer.Ordinal);
        var seenAnonymousUsers = 0;

        for (var i = messages.Count - 1; i >= 0; i--)
        {
            var message = messages[i];
            if (message.Role != ChatRole.User)
                continue;

            var turnId = GetMessageTurnId(message);
            var isNewUserTurn = string.IsNullOrWhiteSpace(turnId)
                ? ++seenAnonymousUsers <= preserveRecentUserTurnCount
                : seenUserTurnIds.Add(turnId!);

            if (!isNewUserTurn)
                continue;

            var userTurnCount = seenUserTurnIds.Count + seenAnonymousUsers;
            if (userTurnCount >= preserveRecentUserTurnCount)
                return messages.Count - i;
        }

        return messages.Count;
    }

    private static int ResolvePreserveFromBoundaryRawMessageCount(
        IReadOnlyList<ChatMessage> messages,
        string? messageId,
        string? messageTurnId)
    {
        if (!string.IsNullOrWhiteSpace(messageTurnId))
        {
            var turnIndex = FindFirstMessageTurnIndex(messages, messageTurnId);
            if (turnIndex >= 0)
                return messages.Count - turnIndex;
        }

        if (!string.IsNullOrWhiteSpace(messageId))
        {
            var messageIndex = FindMessageIndex(messages, messageId);
            if (messageIndex >= 0)
            {
                var turnId = GetMessageTurnId(messages[messageIndex]);
                if (!string.IsNullOrWhiteSpace(turnId))
                {
                    var turnIndex = FindFirstMessageTurnIndex(messages, turnId);
                    return messages.Count - (turnIndex >= 0 ? turnIndex : messageIndex);
                }

                return messages.Count - messageIndex;
            }
        }

        return messages.Count;
    }

    private static int FindMessageIndex(IReadOnlyList<ChatMessage> messages, string messageId)
    {
        for (var i = 0; i < messages.Count; i++)
        {
            if (string.Equals(messages[i].MessageId, messageId, StringComparison.Ordinal))
                return i;
        }

        return -1;
    }

    private static int FindFirstMessageTurnIndex(IReadOnlyList<ChatMessage> messages, string messageTurnId)
    {
        for (var i = 0; i < messages.Count; i++)
        {
            if (string.Equals(GetMessageTurnId(messages[i]), messageTurnId, StringComparison.Ordinal))
                return i;
        }

        return -1;
    }

    private static string? GetMessageTurnId(ChatMessage message) =>
        message.AdditionalProperties?.TryGetValue<string>(
            ThreadHistoryCompactionMetadata.MessageTurnIdPropertyName,
            out var turnId) == true
            ? turnId
            : null;

    private static string CreateHandoffSummarizationPrompt(bool useSingleSummary)
    {
        var summaryContinuityInstruction = useSingleSummary
            ? "Incorporate any previous summary as authoritative context and emit one consolidated handoff."
            : "Preserve any previous summary as a distinct prior-memory layer when it materially affects the handoff.";

        return $$"""
        Generate a compact handoff summary for another AI agent that will continue this conversation.

        Preserve the information needed to resume work without rereading the compacted messages:
        - the user's current goal and any important corrections or preferences;
        - decisions already made and rejected paths;
        - relevant files, symbols, commands, tools, errors, and test results;
        - current progress and remaining work;
        - durable facts, constraints, examples, and references that affect the next response.

        {{summaryContinuityInstruction}} Do not invent facts, critique the conversation, or omit concrete
        details that would be expensive or impossible to recover later.
        """;
    }
}


#region Middleware Extensions
/// <summary>
/// Extension methods for configuring middleware for the AgentBuilder.
/// </summary>
public static class AgentBuilderMiddlewareExtensions
{
    /// <summary>
    /// Adds a unified agent middleware instance.
    /// Supports Collapsing via extension methods (.AsGlobal(), .ForToolHarness(), .ForSkill(), .ForFunction()).
    /// </summary>
    /// <param name="builder">The agent builder</param>
    /// <param name="middleware">The unified middleware to add</param>
    /// <returns>The builder for chaining</returns>
    public static AgentBuilder WithMiddleware(this AgentBuilder builder, Middleware.IAgentMiddleware middleware)
    {
        if (middleware != null)
        {
            builder.Middlewares.Add(middleware);
        }
        return builder;
    }

    /// <summary>
    /// Adds a unified agent middleware by type (will be instantiated).
    /// </summary>
    /// <typeparam name="T">The middleware type</typeparam>
    /// <param name="builder">The agent builder</param>
    /// <returns>The builder for chaining</returns>
    public static AgentBuilder WithMiddleware<T>(this AgentBuilder builder)
        where T : Middleware.IAgentMiddleware, new()
        => builder.WithMiddleware(new T());

    /// <summary>
    /// Adds multiple unified agent middlewares.
    /// </summary>
    /// <param name="builder">The agent builder</param>
    /// <param name="middlewares">The middlewares to add</param>
    /// <returns>The builder for chaining</returns>
    public static AgentBuilder WithMiddlewares(this AgentBuilder builder, params Middleware.IAgentMiddleware[] middlewares)
    {
        if (middlewares != null)
        {
            foreach (var middleware in middlewares)
            {
                builder.WithMiddleware(middleware);
            }
        }
        return builder;
    }

    /// <summary>
    /// Adds circuit breaker middleware to prevent infinite loops from repeated identical tool calls.
    /// </summary>
    /// <param name="builder">The agent builder</param>
    /// <param name="maxConsecutiveCalls">Maximum consecutive identical calls before triggering (default: 3)</param>
    /// <returns>The builder for chaining</returns>
    /// <remarks>
    /// The circuit breaker detects when the same tool is called with identical arguments
    /// multiple times consecutively, which typically indicates the agent is stuck in a loop.
    /// When triggered, execution terminates with a descriptive message.
    /// </remarks>
    /// <example>
    /// <code>
    /// var agent = new AgentBuilder()
    ///     .WithCircuitBreaker(maxConsecutiveCalls: 3)
    ///     .Build();
    /// </code>
    /// </example>
    public static AgentBuilder WithCircuitBreaker(this AgentBuilder builder, int maxConsecutiveCalls = 3)
    {
        var middleware = new CircuitBreakerMiddleware
        {
            MaxConsecutiveCalls = maxConsecutiveCalls
        };
        builder.Middlewares.Add(middleware);
        return builder;
    }

    /// <summary>
    /// Adds circuit breaker middleware with custom configuration.
    /// </summary>
    /// <param name="builder">The agent builder</param>
    /// <param name="configure">Action to configure the circuit breaker middleware</param>
    /// <returns>The builder for chaining</returns>
    /// <example>
    /// <code>
    /// var agent = new AgentBuilder()
    ///     .WithCircuitBreaker(config =>
    ///     {
    ///         config.MaxConsecutiveCalls = 5;
    ///         config.TerminationMessageTemplate = "Loop detected for {toolName}. Stopping.";
    ///     })
    ///     .Build();
    /// </code>
    /// </example>
    public static AgentBuilder WithCircuitBreaker(this AgentBuilder builder, Action<CircuitBreakerMiddleware> configure)
    {
        var middleware = new CircuitBreakerMiddleware();
        configure(middleware);
        builder.Middlewares.Add(middleware);
        return builder;
    }

    /// <summary>
    /// Adds error tracking middleware to detect and handle consecutive tool execution errors.
    /// Terminates execution when errors exceed the specified threshold.
    /// </summary>
    /// <param name="builder">The agent builder</param>
    /// <param name="maxConsecutiveErrors">Maximum consecutive errors before termination (default: 3)</param>
    /// <returns>The builder for chaining</returns>
    /// <example>
    /// <code>
    /// var agent = new AgentBuilder()
    ///     .WithErrorTracking(maxConsecutiveErrors: 5)
    ///     .Build();
    /// </code>
    /// </example>
    public static AgentBuilder WithErrorTracking(this AgentBuilder builder, int maxConsecutiveErrors = 3)
    {
        var middleware = new ErrorTrackingMiddleware
        {
            MaxConsecutiveErrors = maxConsecutiveErrors
        };
        builder.Middlewares.Add(middleware);
        return builder;
    }

    /// <summary>
    /// Adds error tracking middleware with custom configuration.
    /// </summary>
    /// <param name="builder">The agent builder</param>
    /// <param name="configure">Action to configure the error tracking middleware</param>
    /// <returns>The builder for chaining</returns>
    /// <example>
    /// <code>
    /// var agent = new AgentBuilder()
    ///     .WithErrorTracking(config =>
    ///     {
    ///         config.MaxConsecutiveErrors = 5;
    ///         config.CustomErrorDetector = result =>
    ///             result.Exception != null ||
    ///             result.Result?.ToString()?.Contains("FATAL") == true;
    ///     })
    ///     .Build();
    /// </code>
    /// </example>
    public static AgentBuilder WithErrorTracking(this AgentBuilder builder, Action<ErrorTrackingMiddleware> configure)
    {
        var middleware = new ErrorTrackingMiddleware();
        configure(middleware);
        builder.Middlewares.Add(middleware);
        return builder;
    }

    /// <summary>
    /// Adds total error threshold middleware to protect against gradual degradation.
    /// Tracks total errors across all iterations (regardless of type) and stops when threshold is exceeded.
    /// This complements ErrorTracking (consecutive same errors) and CircuitBreaker (identical calls).
    /// </summary>
    /// <param name="builder">The agent builder</param>
    /// <param name="maxTotalErrors">Maximum total errors before termination (default: 10)</param>
    /// <returns>The builder for chaining</returns>
    /// <remarks>
    /// Use this middleware when you want to protect against:
    /// - Different types of errors occurring progressively
    /// - Total degradation from mixed failure scenarios
    /// - Agents that keep trying despite multiple different problems
    /// </remarks>
    /// <example>
    /// <code>
    /// var agent = new AgentBuilder()
    ///     .WithErrorTracking(maxConsecutiveErrors: 3)           // 3 consecutive same errors
    ///     .WithCircuitBreaker(maxConsecutiveCalls: 3)           // 3 identical tool calls
    ///     .WithTotalErrorThreshold(maxTotalErrors: 10)          // 10 total errors (any type)
    ///     .Build();
    /// </code>
    /// </example>
    public static AgentBuilder WithTotalErrorThreshold(this AgentBuilder builder, int maxTotalErrors = 10)
    {
        var middleware = new TotalErrorThresholdMiddleware
        {
            MaxTotalErrors = maxTotalErrors
        };
        builder.Middlewares.Add(middleware);
        return builder;
    }

    /// <summary>
    /// Adds total error threshold middleware with custom configuration.
    /// </summary>
    /// <param name="builder">The agent builder</param>
    /// <param name="configure">Action to configure the total error threshold middleware</param>
    /// <returns>The builder for chaining</returns>
    /// <example>
    /// <code>
    /// var agent = new AgentBuilder()
    ///     .WithTotalErrorThreshold(config =>
    ///     {
    ///         config.MaxTotalErrors = 15;
    ///         config.CustomErrorDetector = result =>
    ///             result.Exception != null ||
    ///             result.Result?.ToString()?.Contains("CRITICAL") == true;
    ///     })
    ///     .Build();
    /// </code>
    /// </example>
    public static AgentBuilder WithTotalErrorThreshold(this AgentBuilder builder, Action<TotalErrorThresholdMiddleware> configure)
    {
        var middleware = new TotalErrorThresholdMiddleware();
        configure(middleware);
        builder.Middlewares.Add(middleware);
        return builder;
    }

    //
    // FUNCTION-LEVEL ERROR HANDLING MIDDLEWARE
    //

    /// <summary>
    /// Adds function RetryMiddleware  with provider-aware retry logic.
    /// Uses settings from AgentConfig.ErrorHandling for retry behavior.
    /// </summary>
    /// <param name="builder">The agent builder</param>
    /// <returns>The builder for chaining</returns>
    /// <remarks>
    /// <para>
    /// This middleware provides intelligent retry logic with a 3-tier priority system:
    /// </para>
    /// <list type="number">
    /// <item><b>Priority 1:</b> Custom retry strategy (if configured via ErrorHandling.CustomRetryStrategy)</item>
    /// <item><b>Priority 2:</b> Provider-aware handling (respects Retry-After headers, error categorization)</item>
    /// <item><b>Priority 3:</b> Exponential backoff fallback (with jitter)</item>
    /// </list>
    /// <para>
    /// <b>Recommended Middleware Order:</b>
    /// </para>
    /// <code>
    /// .WithFunctionRetry()    // Outermost - retry the entire timeout operation
    /// .WithFunctionTimeout()  // Middle - timeout individual attempts
    /// .WithPermissions()      // Innermost - check permissions before execution
    /// </code>
    /// <para>
    /// The middleware uses settings from <c>AgentConfig.ErrorHandling</c>:
    /// - MaxRetries (default: 3)
    /// - RetryDelay (default: 1 second)
    /// - BackoffMultiplier (default: 2.0)
    /// - MaxRetryDelay (default: 30 seconds)
    /// - UseProviderRetryDelays (default: true)
    /// - MaxRetriesByCategory (optional per-category limits)
    /// - CustomRetryStrategy (optional override)
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// var config = new AgentConfig
    /// {
    ///     ErrorHandling = new ErrorHandlingConfig
    ///     {
    ///         MaxRetries = 5,
    ///         RetryDelay = TimeSpan.FromSeconds(2)
    ///     }
    /// };
    ///
    /// var agent = new AgentBuilder(config)
    ///     .WithFunctionRetry()  // Uses config.ErrorHandling settings
    ///     .Build();
    /// </code>
    /// </example>
    public static AgentBuilder WithFunctionRetry(this AgentBuilder builder)
    {
        var config = builder.Config.ErrorHandling ?? new ErrorHandlingConfig();
        // Note: When manually adding via extension method, no provider-specific error handler is available.
        // The middleware will use GenericErrorHandler. If provider-specific handling is needed,
        // use the automatic registration in Build() which has access to the provider.
        var middleware = new RetryMiddleware(config);
        builder.Middlewares.Add(middleware);
        return builder;
    }

    /// <summary>
    /// Adds function RetryMiddleware  with custom error handling configuration.
    /// </summary>
    /// <param name="builder">The agent builder</param>
    /// <param name="configure">Action to configure error handling settings</param>
    /// <returns>The builder for chaining</returns>
    /// <example>
    /// <code>
    /// var agent = new AgentBuilder()
    ///     .WithFunctionRetry(config =>
    ///     {
    ///         config.MaxRetries = 5;
    ///         config.RetryDelay = TimeSpan.FromSeconds(2);
    ///         config.MaxRetriesByCategory = new Dictionary&lt;ErrorCategory, int&gt;
    ///         {
    ///             [ErrorCategory.RateLimitRetryable] = 10,
    ///             [ErrorCategory.ServerError] = 3
    ///         };
    ///     })
    ///     .Build();
    /// </code>
    /// </example>
    public static AgentBuilder WithFunctionRetry(this AgentBuilder builder, Action<ErrorHandlingConfig> configure)
    {
        var config = new ErrorHandlingConfig();
        configure(config);
        // Note: When manually adding via extension method, no provider-specific error handler is available.
        // The middleware will use GenericErrorHandler. If provider-specific handling is needed,
        // use the automatic registration in Build() which has access to the provider.
        var middleware = new RetryMiddleware(config);
        builder.Middlewares.Add(middleware);
        return builder;
    }

    /// <summary>
    /// Adds function timeout middleware to enforce execution time limits.
    /// Uses SingleFunctionTimeout from AgentConfig.ErrorHandling (default: 30 seconds).
    /// </summary>
    /// <param name="builder">The agent builder</param>
    /// <returns>The builder for chaining</returns>
    /// <remarks>
    /// <para>
    /// If a function takes longer than the configured timeout, it will be cancelled
    /// and a TimeoutException will be thrown.
    /// </para>
    /// <para>
    /// <b>Recommended Middleware Order:</b>
    /// </para>
    /// <code>
    /// .WithFunctionRetry()    // Outermost - retry the entire timeout operation
    /// .WithFunctionTimeout()  // Middle - timeout individual attempts
    /// .WithPermissions()      // Innermost - check permissions before execution
    /// </code>
    /// <para>
    /// When combined with RetryMiddleware , the timeout applies to EACH retry attempt
    /// independently, not to the total time across all attempts.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// var config = new AgentConfig
    /// {
    ///     ErrorHandling = new ErrorHandlingConfig
    ///     {
    ///         SingleFunctionTimeout = TimeSpan.FromMinutes(2)
    ///     }
    /// };
    ///
    /// var agent = new AgentBuilder(config)
    ///     .WithFunctionTimeout()  // Uses config.ErrorHandling.SingleFunctionTimeout
    ///     .Build();
    /// </code>
    /// </example>
    public static AgentBuilder WithFunctionTimeout(this AgentBuilder builder)
    {
        var timeout = builder.Config.ErrorHandling?.SingleFunctionTimeout ?? TimeSpan.FromSeconds(30);
        var middleware = new FunctionTimeoutMiddleware(timeout);
        builder.Middlewares.Add(middleware);
        return builder;
    }

    /// <summary>
    /// Adds function timeout middleware with a custom timeout value.
    /// </summary>
    /// <param name="builder">The agent builder</param>
    /// <param name="timeout">Maximum time allowed for function execution</param>
    /// <returns>The builder for chaining</returns>
    /// <example>
    /// <code>
    /// var agent = new AgentBuilder()
    ///     .WithFunctionTimeout(TimeSpan.FromMinutes(5))
    ///     .Build();
    /// </code>
    /// </example>
    public static AgentBuilder WithFunctionTimeout(this AgentBuilder builder, TimeSpan timeout)
    {
        var middleware = new FunctionTimeoutMiddleware(timeout);
        builder.Middlewares.Add(middleware);
        return builder;
    }

    /// <summary>
    /// Adds error formatting middleware to sanitize function errors before sending to the LLM.
    /// Uses settings from AgentConfig.ErrorHandling.IncludeDetailedErrorsInChat (default: false for security).
    /// </summary>
    /// <param name="builder">The agent builder</param>
    /// <returns>The builder for chaining</returns>
    /// <remarks>
    /// <para>
    /// This middleware acts as a security boundary, preventing sensitive information from being
    /// exposed to the LLM through exception messages. By default, it returns sanitized error
    /// messages like "Error: Function 'X' failed." while preserving the full exception in
    /// <c>AgentMiddlewareContext.FunctionException</c> for logging and debugging.
    /// </para>
    /// <para>
    /// <b>Security Note:</b> The default setting (<c>IncludeDetailedErrorsInChat = false</c>) is
    /// recommended for production to prevent exposing:
    /// - Stack traces
    /// - Database connection strings
    /// - File system paths
    /// - API keys or tokens
    /// </para>
    /// <para>
    /// <b>Recommended Middleware Order:</b>
    /// </para>
    /// <code>
    /// .WithFunctionRetry()      // Outermost - retry the entire operation
    /// .WithFunctionTimeout()    // Middle - timeout individual attempts
    /// .WithErrorFormatting()    // Innermost - format errors after all retries exhausted
    /// </code>
    /// </remarks>
    /// <example>
    /// <code>
    /// // Default - sanitized errors (secure)
    /// var agent = new AgentBuilder(config)
    ///     .WithErrorFormatting()
    ///     .Build();
    ///
    /// // Allow detailed errors (only for trusted environments)
    /// var config = new AgentConfig
    /// {
    ///     ErrorHandling = new ErrorHandlingConfig
    ///     {
    ///         IncludeDetailedErrorsInChat = true
    ///     }
    /// };
    /// var agent = new AgentBuilder(config)
    ///     .WithErrorFormatting()
    ///     .Build();
    /// </code>
    /// </example>
    public static AgentBuilder WithErrorFormatting(this AgentBuilder builder)
    {
        var config = builder.Config.ErrorHandling ?? new ErrorHandlingConfig();
        var middleware = new ErrorFormattingMiddleware(config);
        builder.Middlewares.Add(middleware);
        return builder;
    }

    /// <summary>
    /// Adds error formatting middleware with explicit configuration.
    /// </summary>
    /// <param name="builder">The agent builder</param>
    /// <param name="includeDetailedErrors">Whether to include detailed exception messages in function results sent to the LLM</param>
    /// <returns>The builder for chaining</returns>
    /// <remarks>
    /// <para>
    /// <b>Security Warning:</b> Setting <paramref name="includeDetailedErrors"/> to <c>true</c>
    /// may expose sensitive information to the LLM. Use only in trusted environments or for debugging.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// // Debugging/development - include detailed errors
    /// var agent = new AgentBuilder()
    ///     .WithErrorFormatting(includeDetailedErrors: true)
    ///     .Build();
    ///
    /// // Production - sanitized errors (recommended)
    /// var agent = new AgentBuilder()
    ///     .WithErrorFormatting(includeDetailedErrors: false)
    ///     .Build();
    /// </code>
    /// </example>
    public static AgentBuilder WithErrorFormatting(this AgentBuilder builder, bool includeDetailedErrors)
    {
        var middleware = new ErrorFormattingMiddleware
        {
            IncludeDetailedErrorsInChat = includeDetailedErrors
        };
        builder.Middlewares.Add(middleware);
        return builder;
    }

    /// <summary>
    /// Convenience method that registers all error handling middleware in the correct order.
    /// This includes circuit breaker, error tracking, total error threshold, function retry, function timeout, and error formatting.
    /// </summary>
    /// <param name="builder">The agent builder</param>
    /// <param name="maxConsecutiveCalls">Maximum consecutive identical function calls before circuit breaker triggers (default: 5)</param>
    /// <param name="maxConsecutiveErrors">Maximum consecutive errors before termination (default: 3)</param>
    /// <param name="maxTotalErrors">Maximum total errors across all iterations before termination (default: 10)</param>
    /// <returns>The builder for chaining</returns>
    /// <remarks>
    /// <para>
    /// This convenience method registers middleware in the optimal order:
    /// </para>
    /// <para><b>Iteration-level middleware (outer to inner):</b></para>
    /// <list type="number">
    /// <item><b>CircuitBreakerMiddleware</b> - Detects stuck loops (same function called N times)</item>
    /// <item><b>ErrorTrackingMiddleware</b> - Tracks consecutive errors (resets on success)</item>
    /// <item><b>TotalErrorThresholdMiddleware</b> - Tracks cumulative errors (never resets)</item>
    /// </list>
    /// <para><b>Function-level middleware (onion pattern):</b></para>
    /// <list type="number">
    /// <item><b>FunctionRetryMiddleware</b> - Outermost, retries entire operation</item>
    /// <item><b>FunctionTimeoutMiddleware</b> - Middle, applies timeout to each retry attempt</item>
    /// <item><b>ErrorFormattingMiddleware</b> - Innermost, sanitizes errors for LLM (security boundary)</item>
    /// </list>
    /// <para>
    /// Function-level middleware uses settings from <c>AgentConfig.ErrorHandling</c> for retry/timeout/formatting configuration.
    /// </para>
    /// <para>
    /// <b>Security Note:</b> By default, error messages sent to the LLM are sanitized to prevent exposing
    /// sensitive information. Set <c>ErrorHandlingConfig.IncludeDetailedErrorsInChat = true</c> only in
    /// trusted environments.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// // Simple usage with defaults
    /// var agent = new AgentBuilder(config)
    ///     .WithErrorHandling()
    ///     .Build();
    ///
    /// // Custom thresholds
    /// var agent = new AgentBuilder(config)
    ///     .WithErrorHandling(
    ///         maxConsecutiveCalls: 3,
    ///         maxConsecutiveErrors: 5,
    ///         maxTotalErrors: 15)
    ///     .Build();
    /// </code>
    /// </example>
    public static AgentBuilder WithErrorHandling(
        this AgentBuilder builder,
        int maxConsecutiveCalls = 5,
        int maxConsecutiveErrors = 3,
        int maxTotalErrors = 10)
    {
        // Iteration-level middleware (order matters: circuit breaker → error tracking → total threshold)
        builder.WithCircuitBreaker(maxConsecutiveCalls);
        builder.WithErrorTracking(maxConsecutiveErrors);
        builder.WithTotalErrorThreshold(maxTotalErrors);

        // Function-level middleware (onion pattern: retry → timeout → formatting)
        // These use AgentConfig.ErrorHandling for retry/timeout/formatting settings
        builder.WithFunctionRetry();
        builder.WithFunctionTimeout();
        builder.WithErrorFormatting();  // Innermost - sanitizes errors for LLM

        return builder;
    }

    /// <summary>
    /// Convenience method that registers all error handling middleware with advanced configuration options.
    /// Allows fine-grained control over each middleware component.
    /// </summary>
    /// <param name="builder">The agent builder</param>
    /// <param name="configureCircuitBreaker">Action to configure circuit breaker middleware</param>
    /// <param name="configureErrorTracking">Optional action to configure error tracking middleware</param>
    /// <param name="configureTotalThreshold">Optional action to configure total error threshold middleware</param>
    /// <param name="configureFunctionRetry">Optional action to configure function RetryMiddleware </param>
    /// <param name="configureFunctionTimeout">Optional timeout for function execution</param>
    /// <param name="includeDetailedErrorsInChat">Optional flag to include detailed error messages in LLM chat (default: false for security)</param>
    /// <returns>The builder for chaining</returns>
    /// <remarks>
    /// <para>
    /// This overload provides maximum flexibility for configuring error handling middleware.
    /// Middleware that are not configured (null actions) will use sensible defaults.
    /// </para>
    /// <para>
    /// <b>NOTE:</b> This overload requires at least the first parameter (configureCircuitBreaker)
    /// to disambiguate from the simple overload. To use all defaults, use the parameterless
    /// <see cref="WithErrorHandling(AgentBuilder, int, int, int)"/> overload instead.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// var agent = new AgentBuilder(config)
    ///     .WithErrorHandling(
    ///         configureCircuitBreaker: cb =>
    ///         {
    ///             cb.MaxConsecutiveCalls = 3;
    ///             cb.TerminationMessageTemplate = "Loop detected for {toolName}!";
    ///         },
    ///         configureFunctionRetry: retry =>
    ///         {
    ///             retry.MaxRetries = 5;
    ///             retry.RetryDelay = TimeSpan.FromSeconds(2);
    ///             retry.MaxRetriesByCategory = new Dictionary&lt;ErrorCategory, int&gt;
    ///             {
    ///                 [ErrorCategory.RateLimitRetryable] = 10
    ///             };
    ///         },
    ///         configureFunctionTimeout: TimeSpan.FromMinutes(2),
    ///         includeDetailedErrorsInChat: false)  // Secure by default
    ///     .Build();
    /// </code>
    /// </example>
    public static AgentBuilder WithErrorHandling(
        this AgentBuilder builder,
        Action<CircuitBreakerMiddleware> configureCircuitBreaker,
        Action<ErrorTrackingMiddleware>? configureErrorTracking = null,
        Action<TotalErrorThresholdMiddleware>? configureTotalThreshold = null,
        Action<ErrorHandlingConfig>? configureFunctionRetry = null,
        TimeSpan? configureFunctionTimeout = null,
        bool? includeDetailedErrorsInChat = null)
    {
        // Iteration-level middleware
        if (configureCircuitBreaker != null)
            builder.WithCircuitBreaker(configureCircuitBreaker);
        else
            builder.WithCircuitBreaker(maxConsecutiveCalls: 5);

        if (configureErrorTracking != null)
            builder.WithErrorTracking(configureErrorTracking);
        else
            builder.WithErrorTracking(maxConsecutiveErrors: 3);

        if (configureTotalThreshold != null)
            builder.WithTotalErrorThreshold(configureTotalThreshold);
        else
            builder.WithTotalErrorThreshold(maxTotalErrors: 10);

        // Function-level middleware
        if (configureFunctionRetry != null)
            builder.WithFunctionRetry(configureFunctionRetry);
        else
            builder.WithFunctionRetry();

        if (configureFunctionTimeout.HasValue)
            builder.WithFunctionTimeout(configureFunctionTimeout.Value);
        else
            builder.WithFunctionTimeout();

        // Error formatting (innermost - security boundary)
        if (includeDetailedErrorsInChat.HasValue)
            builder.WithErrorFormatting(includeDetailedErrorsInChat.Value);
        else
            builder.WithErrorFormatting();

        return builder;
    }

    //
    // PII PROTECTION
    //

    /// <summary>
    /// Adds PII (Personally Identifiable Information) protection middleware
    /// with default settings. Detects and handles email, credit cards, SSN,
    /// phone numbers, and IP addresses.
    /// </summary>
    /// <param name="builder">The agent builder</param>
    /// <returns>The builder for chaining</returns>
    /// <remarks>
    /// Default strategies:
    /// - Email: Redact → [EMAIL_REDACTED]
    /// - Credit Card: Block (throws PIIBlockedException)
    /// - SSN: Block (throws PIIBlockedException)
    /// - Phone: Mask → ***-***-1234
    /// - IP Address: Hash → &lt;ip_hash:a1b2c3d4&gt;
    /// </remarks>
    /// <example>
    /// <code>
    /// var agent = new AgentBuilder()
    ///     .WithPIIProtection()
    ///     .Build();
    /// </code>
    /// </example>
    public static AgentBuilder WithPIIProtection(this AgentBuilder builder)
    {
        var middleware = new PIIMiddleware();
        // Insert at the beginning so PII is sanitized before other middlewares see the messages
        builder.Middlewares.Insert(0, middleware);
        return builder;
    }

    /// <summary>
    /// Adds PII protection middleware with custom configuration.
    /// Allows per-type strategy configuration and custom detectors.
    /// </summary>
    /// <param name="builder">The agent builder</param>
    /// <param name="configure">Action to configure the PII middleware</param>
    /// <returns>The builder for chaining</returns>
    /// <example>
    /// <code>
    /// var agent = new AgentBuilder()
    ///     .WithPIIProtection(config =>
    ///     {
    ///         // Configure strategies per PII type
    ///         config.EmailStrategy = PIIStrategy.Redact;
    ///         config.CreditCardStrategy = PIIStrategy.Block;
    ///         config.SSNStrategy = PIIStrategy.Block;
    ///         config.PhoneStrategy = PIIStrategy.Mask;
    ///         config.IPAddressStrategy = PIIStrategy.Hash;
    ///
    ///         // Also scan LLM output (in case it echoes PII)
    ///         config.ApplyToOutput = true;
    ///
    ///         // Add custom detector for employee IDs
    ///         config.AddCustomDetector(
    ///             name: "EmployeeId",
    ///             pattern: @"EMP-\d{6}",
    ///             strategy: PIIStrategy.Redact);
    ///     })
    ///     .Build();
    /// </code>
    /// </example>
    public static AgentBuilder WithPIIProtection(this AgentBuilder builder, Action<PIIMiddleware> configure)
    {
        var middleware = new PIIMiddleware();
        configure(middleware);
        // Insert at the beginning so PII is sanitized before other middlewares see the messages
        builder.Middlewares.Insert(0, middleware);
        return builder;
    }

    //
    // TOOL Collapsing
    //

    /// <summary>
    /// Enables tool Collapsing middleware for ToolHarness collapsing and skills architecture.
    /// When enabled, ToolHarnesses and skills are hidden behind container functions,
    /// reducing the initial tool list and cognitive load on the LLM.
    /// </summary>
    /// <param name="builder">The agent builder</param>
    /// <returns>The builder for chaining</returns>
    /// <remarks>
    /// <para>
    /// Tool Collapsing allows you to organize functions hierarchically:
    /// - ToolHarness containers: Hide member functions until ToolHarness is expanded
    /// - Skill containers: Hide skill-specific functions until skill is activated
    /// </para>
    /// <para>
    /// This can reduce initial tool list size by up to 87.5%, improving LLM performance
    /// and reducing token usage.
    /// </para>
    /// <para>
    /// <b>Phase 1 Note:</b> This middleware integrates with the existing Collapsing
    /// infrastructure in Agent.cs. Future phases will migrate more logic to middleware.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// var agent = new AgentBuilder()
    ///     .WithTools&lt;FinancialToolHarness&gt;()
    ///     .WithHarnessCollapsing()  // Enable tool Collapsing
    ///     .Build();
    /// </code>
    /// </example>
    public static AgentBuilder WithHarnessCollapsing(this AgentBuilder builder)
    {
        // Enable Collapsing in config
        builder.Config.Collapsing ??= new CollapsingConfig();
        builder.Config.Collapsing.Enabled = true;

        // NOTE: The ToolCollapsingMiddleware will be instantiated and added to the pipeline
        // during Build() after the Agent is constructed (since it needs the ToolVisibilityManager
        // which is created in the Agent constructor). See Build() for registration logic.

        return builder;
    }

    /// <summary>
    /// Enables tool Collapsing middleware with custom configuration.
    /// </summary>
    /// <param name="builder">The agent builder</param>
    /// <param name="configure">Action to configure Collapsing behavior</param>
    /// <returns>The builder for chaining</returns>
    /// <example>
    /// <code>
    /// var agent = new AgentBuilder()
    ///     .WithTools&lt;FinancialToolHarness&gt;()
    ///     .WithHarnessCollapsing(config =>
    ///     {
    ///         config.CollapseClientTools = true;
    ///         config.MaxFunctionNamesInDescription = 5;
    ///     })
    ///     .Build();
    /// </code>
    /// </example>
    public static AgentBuilder WithHarnessCollapsing(this AgentBuilder builder, Action<CollapsingConfig> configure)
    {
        builder.Config.Collapsing ??= new CollapsingConfig();
        builder.Config.Collapsing.Enabled = true;
        configure(builder.Config.Collapsing);

        // NOTE: The ToolCollapsingMiddleware will be instantiated and added to the pipeline
        // during Build() after the Agent is constructed. See Build() for registration logic.

        return builder;
    }

    /// <summary>
    /// Disables tool Collapsing, making all tools visible to the LLM at all times.
    /// </summary>
    /// <param name="builder">The agent builder</param>
    /// <returns>The builder for chaining</returns>
    /// <remarks>
    /// Use this when you want all functions to be immediately available without
    /// requiring container expansion. This may increase token usage but simplifies
    /// tool discovery.
    /// </remarks>
    /// <example>
    /// <code>
    /// var agent = new AgentBuilder()
    ///     .WithTools&lt;FinancialToolHarness&gt;()
    ///     .WithoutToolCollapsing()  // All tools always visible
    ///     .Build();
    /// </code>
    /// </example>
    public static AgentBuilder WithoutToolCollapsing(this AgentBuilder builder)
    {
        builder.Config.Collapsing ??= new CollapsingConfig();
        builder.Config.Collapsing.Enabled = false;
        return builder;
    }
}

#endregion

#region Memory Extensions
/// <summary>
/// Extension methods for configuring agent-specific memory capabilities.
/// </summary>
public static class AgentBuilderMemoryExtensions
{
    /// <summary>
    /// Configures the agent's deep, static, read-only knowledge base.
    /// This utilizes an Indexed Retrieval (RAG) system for the agent's core expertise.
    /// </summary>
    ///


    /// <summary>
    /// Configures compaction to manage model context and optional durable thread history.
    /// </summary>
    /// <param name="builder">The agent builder instance</param>
    /// <param name="configure">Configuration action for compaction settings</param>
    /// <returns>The builder for method chaining</returns>
    /// <example>
    /// <code>
    /// builder.WithCompaction(config => {
    ///     config.Enabled = true;
    ///     config.Strategy = new SummarizingCompactionOptions { PreserveRecentUserTurnCount = 5 };
    ///     config.Trigger = new CountCompactionTriggerOptions { Threshold = 10 };
    ///     config.Retention = new PreserveThreadHistoryOptions();
    /// });
    /// </code>
    /// </example>
    public static AgentBuilder WithCompaction(this AgentBuilder builder, Action<CompactionConfig>? configure = null)
    {
        var config = builder.Config.Compaction ?? new CompactionConfig();
        configure?.Invoke(config);

        builder.Config.Compaction = config;
        return builder;
    }
}
#endregion

#region ToolHarness Extensions


/// <summary>
/// Extension methods for configuring ToolHarnesses for the AgentBuilder.
/// </summary>
public static class AgentBuilderToolHarnessExtensions
{
    /// <summary>
    /// Loads generated toolharness, middleware, and middleware-state catalogs from the assembly containing <typeparamref name="T"/>.
    /// Use this when config names toolharnesses that should be resolved from a referenced assembly without registering
    /// every toolharness eagerly in code.
    /// </summary>
    public static AgentBuilder WithToolHarnessCatalogFrom<T>(this AgentBuilder builder)
    {
        RuntimeHelpers.RunModuleConstructor(typeof(T).Assembly.ManifestModule.ModuleHandle);
        builder.LoadToolRegistryFromAssembly(typeof(T).Assembly);
        builder.LoadGeneratedRegistries();
        return builder;
    }

    /// <summary>
    /// Registers one generated function from the specified toolharness.
    /// AOT-Compatible: Uses generated ToolHarnessRegistry.All catalog and filters by generated function name.
    /// </summary>
    /// <exception cref="ArgumentException">Thrown when the function name is empty or the qualified toolharness name does not match <typeparamref name="T"/>.</exception>
    /// <exception cref="InvalidOperationException">Thrown when the toolharness or generated function is not found.</exception>
    public static AgentBuilder WithTool<T>(this AgentBuilder builder, string functionName, IToolMetadata? context = null) where T : class, new()
    {
        RuntimeHelpers.RunModuleConstructor(typeof(T).Assembly.ManifestModule.ModuleHandle);
        builder.LoadToolRegistryFromAssembly(typeof(T).Assembly);
        builder.LoadGeneratedRegistries();

        var toolharnessName = typeof(T).Name;
        var (qualifiedToolHarnessName, toolName) = ParseToolReference(functionName, toolharnessName);

        if (!string.Equals(qualifiedToolHarnessName, toolharnessName, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                $"Tool reference '{functionName}' targets toolharness '{qualifiedToolHarnessName}', but the generic type is '{toolharnessName}'.",
                nameof(functionName));
        }

        return builder.WithTool(typeof(T), toolName, context);
    }

    /// <summary>
    /// Registers one generated function from the specified toolharness type.
    /// AOT-Compatible: Uses generated ToolHarnessRegistry.All catalog and filters by generated function name.
    /// </summary>
    /// <exception cref="ArgumentException">Thrown when the function name is empty or the qualified toolharness name does not match <paramref name="toolharnessType"/>.</exception>
    /// <exception cref="InvalidOperationException">Thrown when the toolharness or generated function is not found.</exception>
    public static AgentBuilder WithTool(this AgentBuilder builder, Type toolharnessType, string functionName, IToolMetadata? context = null)
    {
        RuntimeHelpers.RunModuleConstructor(toolharnessType.Assembly.ManifestModule.ModuleHandle);
        builder.LoadToolRegistryFromAssembly(toolharnessType.Assembly);
        builder.LoadGeneratedRegistries();

        var toolharnessName = toolharnessType.Name;
        var (qualifiedToolHarnessName, toolName) = ParseToolReference(functionName, toolharnessName);

        if (!string.Equals(qualifiedToolHarnessName, toolharnessName, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                $"Tool reference '{functionName}' targets toolharness '{qualifiedToolHarnessName}', but the toolharness type is '{toolharnessName}'.",
                nameof(functionName));
        }

        var factory = GetToolHarnessFactory(builder, toolharnessType, toolharnessName);
        RegisterGeneratedTool(builder, factory, toolName, context);
        return builder;
    }

    /// <summary>
    /// Registers one generated function using a qualified "ToolHarnessName.FunctionName" reference.
    /// AOT-Compatible: Uses generated ToolHarnessRegistry.All catalog and filters by generated function name.
    /// </summary>
    /// <exception cref="ArgumentException">Thrown when the tool reference is not in "ToolHarnessName.FunctionName" format.</exception>
    /// <exception cref="InvalidOperationException">Thrown when the toolharness or generated function is not found.</exception>
    public static AgentBuilder WithTool(this AgentBuilder builder, string toolReference, IToolMetadata? context = null)
    {
        builder.LoadGeneratedRegistries();

        var (toolharnessName, toolName) = ParseQualifiedToolReference(toolReference);
        var factory = GetToolHarnessFactory(builder, toolharnessName);
        RegisterGeneratedTool(builder, factory, toolName, context);
        return builder;
    }

    /// <summary>
    /// Registers a toolharness by type with optional execution context.
    /// AOT-Compatible: Uses generated ToolHarnessRegistry.All catalog (zero reflection in hot path).
    /// Automatically loads toolharness registry from the assembly where T is defined if not already loaded.
    /// Auto-registers referenced toolharnesses from skills via GetReferencedToolHarnesses().
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown if toolharness is not found in any loaded registry.</exception>
    public static AgentBuilder WithToolHarness<T>(this AgentBuilder builder, IToolMetadata? context = null) where T : class, new()
    {
        RuntimeHelpers.RunModuleConstructor(typeof(T).Assembly.ManifestModule.ModuleHandle);
        builder.LoadToolRegistryFromAssembly(typeof(T).Assembly);
        builder.LoadGeneratedRegistries();
        var toolharnessName = typeof(T).Name;

        var factory = GetToolHarnessFactory(builder, typeof(T), toolharnessName);

        // AOT-compatible path: Use catalog
        builder._selectedToolHarnessFactories.Add(factory);

        // Track as explicitly registered (for ToolVisibilityManager)
        builder._explicitlyRegisteredToolHarnesses.Add(toolharnessName);

        // Store context
        builder.ToolHarnessContexts[toolharnessName] = context;

        // Auto-discover skill dependencies using catalog (zero reflection)
        AutoRegisterDependenciesFromFactory(builder, factory);

        return builder;
    }

    private static ToolHarnessFactory GetToolHarnessFactory(AgentBuilder builder, string toolharnessName)
    {
        if (!builder._availableToolHarnesses.TryGetValue(toolharnessName, out var factory))
        {
            throw new InvalidOperationException(
                $"ToolHarness '{toolharnessName}' not found in ToolHarnessRegistry.All. " +
                $"Ensure the toolharness class has [AIFunction], [Skill], or [SubAgent] attributes and the source generator ran successfully.");
        }

        return factory;
    }

    private static ToolHarnessFactory GetToolHarnessFactory(AgentBuilder builder, Type toolharnessType, string toolharnessName)
    {
        if (builder._availableToolHarnesses.TryGetValue(toolharnessName, out var factory))
        {
            return factory;
        }

        if (ReflectionToolFactory.TryCreateToolHarnessFactory(toolharnessType, out var reflectionFactory, out var reflectionError))
        {
            builder._availableToolHarnesses[toolharnessName] = reflectionFactory;
            return reflectionFactory;
        }

        throw new InvalidOperationException(
            reflectionError ??
            $"ToolHarness '{toolharnessName}' not found in ToolHarnessRegistry.All. Ensure the toolharness class has [AIFunction], [Skill], or [SubAgent] attributes and the source generator ran successfully.");
    }

    private static void RegisterGeneratedTool(AgentBuilder builder, ToolHarnessFactory factory, string functionName, IToolMetadata? context)
    {
        var availableFunctions = factory.FunctionNames ?? Array.Empty<string>();
        if (!availableFunctions.Contains(functionName, StringComparer.Ordinal))
        {
            var availableList = availableFunctions.Length == 0
                ? "(none)"
                : string.Join(", ", availableFunctions);

            throw new InvalidOperationException(
                $"Function '{functionName}' was not found on toolharness '{factory.Name}'. " +
                $"Available generated functions: {availableList}.");
        }

        if (!builder._selectedToolHarnessFactories.Any(f => f.Name.Equals(factory.Name, StringComparison.OrdinalIgnoreCase)))
        {
            builder._selectedToolHarnessFactories.Add(factory);
        }

        builder._explicitlyRegisteredToolHarnesses.Add(factory.Name);
        builder.ToolHarnessContexts[factory.Name] = context;

        if (builder._toolFunctionFilters.TryGetValue(factory.Name, out var existingFilter))
        {
            builder._toolFunctionFilters[factory.Name] = existingFilter
                .Concat(new[] { functionName })
                .Distinct(StringComparer.Ordinal)
                .ToArray();
        }
        else
        {
            builder._toolFunctionFilters[factory.Name] = new[] { functionName };
        }

        AutoRegisterDependenciesFromFactory(builder, factory);
    }

    private static (string ToolHarnessName, string FunctionName) ParseToolReference(string functionName, string defaultToolHarnessName)
    {
        if (string.IsNullOrWhiteSpace(functionName))
            throw new ArgumentException("Function name cannot be empty.", nameof(functionName));

        return functionName.Contains('.')
            ? ParseQualifiedToolReference(functionName)
            : (defaultToolHarnessName, functionName);
    }

    private static (string ToolHarnessName, string FunctionName) ParseQualifiedToolReference(string toolReference)
    {
        if (string.IsNullOrWhiteSpace(toolReference))
            throw new ArgumentException("Tool reference cannot be empty.", nameof(toolReference));

        var parts = toolReference.Split('.');
        if (parts.Length != 2 || string.IsNullOrWhiteSpace(parts[0]) || string.IsNullOrWhiteSpace(parts[1]))
        {
            throw new ArgumentException(
                $"Tool reference '{toolReference}' must use 'ToolHarnessName.FunctionName' format.",
                nameof(toolReference));
        }

        return (parts[0], parts[1]);
    }

    /// <summary>
    /// Registers a toolharness and configures per-toolharness options such as DI-provided scoped middleware
    /// . Use this overload when your toolharness-scoped middleware requires constructor
    /// parameters that cannot be expressed via a parameterless constructor in
    /// <c>[Collapse(Middlewares = [typeof(T)])]</c>.
    /// </summary>
    /// <example>
    /// <code>
    /// builder.WithToolHarness&lt;DatabaseToolHarness&gt;(opts =>
    ///     opts.AddScopedMiddleware(new DbAuditMiddleware(sp.GetRequiredService&lt;IAuditLog&gt;())));
    /// </code>
    /// </example>
    public static AgentBuilder WithToolHarness<T>(this AgentBuilder builder, Action<ToolHarnessOptions> configure, IToolMetadata? context = null) where T : class, new()
    {
        // Register the toolharness normally first
        builder.WithToolHarness<T>(context);

        // Apply per-toolharness options
        var options = new ToolHarnessOptions();
        configure(options);

        if (options.ScopedMiddlewares.Count > 0)
        {
            var toolharnessName = typeof(T).Name;
            if (!builder._HARNESScopedMiddlewares.TryGetValue(toolharnessName, out var list))
            {
                list = new List<Middleware.IAgentMiddleware>();
                builder._HARNESScopedMiddlewares[toolharnessName] = list;
            }
            list.AddRange(options.ScopedMiddlewares);
        }

        return builder;
    }

    /// <summary>
    /// Registers a toolharness using a pre-created instance with optional execution context.
    /// Used for DI-required toolharnesses (e.g., AgentPlanToolHarness, DynamicMemoryToolHarness).
    /// The generated ToolHarnessFactory delegate is used for function creation (AOT-compatible).
    /// </summary>
    public static AgentBuilder WithToolHarness<T>(this AgentBuilder builder, T instance, IToolMetadata? context = null) where T : class
    {
        RuntimeHelpers.RunModuleConstructor(typeof(T).Assembly.ManifestModule.ModuleHandle);
        builder.LoadToolRegistryFromAssembly(typeof(T).Assembly);
        builder.LoadGeneratedRegistries();
        var toolharnessName = typeof(T).Name;

        if (!builder._availableToolHarnesses.TryGetValue(toolharnessName, out var factory))
        {
            throw new InvalidOperationException(
                $"ToolHarness '{toolharnessName}' not found in ToolHarnessRegistry.All. " +
                $"Ensure the toolharness class has [AIFunction], [Skill], or [SubAgent] attributes and the source generator ran successfully.");
        }

        // Register as instance registration (will use the generated ToolHarnessFactory delegate for function creation)
        builder._instanceRegistrations.Add(new ToolInstanceRegistration(instance, toolharnessName));
        builder.ToolHarnessContexts[toolharnessName] = context;

        // Track this as explicitly registered
        builder._explicitlyRegisteredToolHarnesses.Add(toolharnessName);

        AutoRegisterDependenciesFromFactory(builder, factory);

        return builder;
    }

    /// <summary>
    /// Registers a toolharness by Type with optional execution context.
    /// AOT-Compatible: Uses generated ToolHarnessRegistry.All catalog (zero reflection in hot path).
    /// Automatically loads toolharness registry from the assembly where toolharnessType is defined if not already loaded.
    /// Auto-registers referenced toolharnesses from skills via GetReferencedToolHarnesses().
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown if toolharness is not found in any loaded registry.</exception>
    public static AgentBuilder WithToolHarness(this AgentBuilder builder, Type toolharnessType, IToolMetadata? context = null)
    {
        RuntimeHelpers.RunModuleConstructor(toolharnessType.Assembly.ManifestModule.ModuleHandle);
        builder.LoadToolRegistryFromAssembly(toolharnessType.Assembly);
        builder.LoadGeneratedRegistries();
        var toolharnessName = toolharnessType.Name;

        var factory = GetToolHarnessFactory(builder, toolharnessType, toolharnessName);

        // AOT-compatible path: Use catalog
        builder._selectedToolHarnessFactories.Add(factory);

        // Track as explicitly registered (for ToolVisibilityManager)
        builder._explicitlyRegisteredToolHarnesses.Add(toolharnessName);

        // Store context
        builder.ToolHarnessContexts[toolharnessName] = context;

        // Auto-discover skill dependencies using catalog (zero reflection)
        AutoRegisterDependenciesFromFactory(builder, factory);

        return builder;
    }

    // ============================================
    // MIDDLEWARE STATE ASSEMBLY REGISTRATION
    // ============================================

    /// <summary>
    /// Explicitly loads middleware state factories from the assembly containing the specified marker type.
    /// Use this for assemblies that have [MiddlewareState] types but no toolharnesses.
    /// For assemblies with toolharnesses, state registries are loaded automatically via WithToolHarness&lt;T&gt;().
    /// </summary>
    /// <typeparam name="TMarker">Any type from the assembly to load states from.</typeparam>
    /// <returns>The builder for chaining.</returns>
    [RequiresUnreferencedCode("State registry loading requires MiddlewareStateRegistry from assembly where TMarker is defined to be preserved.")]
    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "RequiresUnreferencedCode declared on method")]
    public static AgentBuilder WithStateAssembly<TMarker>(this AgentBuilder builder)
    {
        builder.LoadStateRegistryFromAssembly(typeof(TMarker).Assembly);
        return builder;
    }

    /// <summary>
    /// Explicitly loads middleware state factories from the specified assembly.
    /// Use this for assemblies that have [MiddlewareState] types but no toolharnesses.
    /// For assemblies with toolharnesses, state registries are loaded automatically via WithToolHarness&lt;T&gt;().
    /// </summary>
    /// <param name="builder">The agent builder.</param>
    /// <param name="assembly">The assembly to load states from.</param>
    /// <returns>The builder for chaining.</returns>
    [RequiresUnreferencedCode("State registry loading requires MiddlewareStateRegistry from the specified assembly to be preserved.")]
    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "RequiresUnreferencedCode declared on method")]
    public static AgentBuilder WithStateAssembly(this AgentBuilder builder, Assembly assembly)
    {
        builder.LoadStateRegistryFromAssembly(assembly);
        return builder;
    }

    /// <summary>
    /// Auto-registers ToolHarnesses referenced by skills using the ToolHarness catalog (zero reflection).
    /// Phase 4.5: Also stores function filters for selective registration.
    /// </summary>
    private static void AutoRegisterDependenciesFromFactory(AgentBuilder builder, ToolHarnessFactory factory)
    {
        var dependencies = factory.GetReferencedToolHarnesses();

        // Phase 4.5: Get function-specific references for selective registration
        var referencedFunctions = factory.GetReferencedFunctions();

        foreach (var depName in dependencies)
        {
            // Check if already selected
            if (builder._selectedToolHarnessFactories.Any(f => f.Name.Equals(depName, StringComparison.OrdinalIgnoreCase)))
                continue;

            // Look up in catalog
            if (builder._availableToolHarnesses!.TryGetValue(depName, out var depFactory))
            {
                builder._selectedToolHarnessFactories.Add(depFactory);
                // Note: Dependencies are NOT added to _explicitlyRegisteredToolHarnesses
                // This distinction matters for ToolVisibilityManager

                // Phase 4.5: Store function filter if specific functions are referenced
                if (referencedFunctions.TryGetValue(depName, out var functionNames) && functionNames.Length > 0)
                {
                    builder._toolFunctionFilters[depName] = functionNames;
                }

                // Recurse for transitive dependencies
                AutoRegisterDependenciesFromFactory(builder, depFactory);
            }
        }
    }

}


#endregion

#region Configuration Extensions

public static class AgentBuilderConfigExtensions
{
    /// <summary>
    /// Sets a custom configuration source for reading API keys and other settings.
    ///
    ///   OPTIONAL: AgentBuilder automatically loads configuration from:
    ///    - appsettings.json (in current directory)
    ///    - Environment variables
    ///    - User secrets (development only)
    ///
    ///  Only use this method if you need to:
    ///    - Load from a non-standard location
    ///    - Use custom configuration sources
    ///    - Override the default configuration behavior
    ///
    /// Example (custom configuration):
    /// <code>
    /// var customConfig = new ConfigurationBuilder()
    ///     .AddJsonFile("custom.json")
    ///     .AddEnvironmentVariables("MY_APP_")
    ///     .Build();
    ///
    /// var agent = new AgentBuilder(config)
    ///     .WithAPIConfiguration(customConfig)  // Override default
    ///     .Build();
    /// </code>
    /// </summary>
    /// <param name="builder">The agent builder.</param>
    /// <param name="configuration">Configuration instance (e.g., from appsettings.json)</param>
    public static AgentBuilder WithAPIConfiguration(this AgentBuilder builder, IConfiguration configuration)
    {
        builder._configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        return builder;
    }

    /// <summary>
    /// Sets a custom configuration source from a specific JSON file.
    ///
    ///   OPTIONAL: AgentBuilder automatically loads appsettings.json from the current directory.
    ///
    ///  Only use this method if you need to load from a different file or location.
    ///
    /// Example:
    /// <code>
    /// var agent = new AgentBuilder(config)
    ///     .WithAPIConfiguration("config/production.json")
    ///     .Build();
    /// </code>
    /// </summary>
    /// <param name="builder">The agent builder.</param>
    /// <param name="jsonFilePath">Path to the JSON configuration file (e.g., appsettings.json)</param>
    /// <param name="optional">Whether the file is optional (default: false)</param>
    /// <param name="reloadOnChange">Whether to reload configuration when file changes (default: true)</param>
    public static AgentBuilder WithAPIConfiguration(this AgentBuilder builder, string jsonFilePath, bool optional = false, bool reloadOnChange = true)
    {
        if (string.IsNullOrWhiteSpace(jsonFilePath))
            throw new ArgumentException("JSON file path cannot be null or empty.", nameof(jsonFilePath));

        if (!optional && !File.Exists(jsonFilePath))
            throw new FileNotFoundException($"Configuration file not found: {jsonFilePath}");

        try
        {
            var configuration = new ConfigurationBuilder()
                .AddJsonFile(jsonFilePath, optional: optional, reloadOnChange: reloadOnChange)
                .Build();

            builder._configuration = configuration;
            return builder;
        }
        catch (Exception ex) when (!(ex is ArgumentException || ex is FileNotFoundException))
        {
            throw new InvalidOperationException($"Failed to load configuration from '{jsonFilePath}': {ex.Message}", ex);
        }
    }
}

#endregion

#region Provider Extensions

public static class AgentBuilderProviderExtensions
{
    public static AgentBuilder WithProvider(this AgentBuilder builder, string providerKey, string modelName, string? apiKey = null)
    {
        builder.Config.SetChatClientConfig(new ClientProviderConfig
        {
            ProviderKey = providerKey,
            ModelName = modelName,
            ApiKey = apiKey
        });
        return builder;
    }
}

#endregion


internal static class AgentBuilderDefaults
{
    internal static IAgentStore AgentStore { get; } = new InMemoryAgentStore();
}
