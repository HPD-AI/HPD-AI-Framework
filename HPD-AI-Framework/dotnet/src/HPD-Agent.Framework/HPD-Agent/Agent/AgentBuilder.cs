using HPD.Agent.Providers;
using HPD.Agent.Middleware;
using HPD.Agent.Middleware.Function;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using System.Collections.Immutable;
using System.Reflection;
using System.Runtime.CompilerServices;
using HPD.Agent.Validation;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Caching.Distributed;
using System.Text.Json;
using HPD.Agent;
using HPD.Agent.Secrets;

namespace HPD.Agent;

// NOTE: Project Middleware classes are defined in the global namespace with the Project class

/// <summary>
/// Dependencies needed for agent construction
/// </summary>
internal record AgentBuildDependencies(
    IChatClient ClientToUse,
    ChatOptions? MergedOptions,
    ErrorHandling.IProviderErrorHandler ErrorHandler,
    IChatClient? SummarizerClient = null,
    /// <summary>
    /// HttpClients created by AgentBuilder for OpenAPI sources that did not provide their own.
    /// Transferred to Agent and disposed when Agent.Dispose() is called.
    /// </summary>
    IReadOnlyList<HttpClient>? OwnedHttpClients = null);

/// <summary>
/// Builder for creating dual interface agents with sophisticated capabilities
/// This is your equivalent of the AgentBuilder from Semantic Kernel, but for the new architecture
/// </summary>
public class AgentBuilder
{
    // The new central configuration object
    private readonly AgentConfig _config;
    private readonly IProviderRegistry _providerRegistry;

    // Fields that are NOT part of the serializable config remain
    internal IChatClient? _baseClient;
    internal IConfiguration? _configuration;
    internal IToolMetadata? _defaulTMetadata;
    internal bool _deferredProvider; // Skip provider validation - chat client will be provided at runtime

    /// <summary>
    /// Instance-based registrations for DI-required Toolkits (e.g., AgentPlanToolkit, DynamicMemoryToolkit).
    /// These Toolkits cannot be instantiated via the catalog because they require constructor parameters.
    /// </summary>
    public readonly List<ToolInstanceRegistration> _instanceRegistrations = new();
    // store individual Toolkit contexts
    internal readonly Dictionary<string, IToolMetadata?> _toolkitContexts = new();
    // V3: Unified content store for all agent content (skills, knowledge, memory, uploads, artifacts)
    internal IContentStore? _contentStore;
    // Track explicitly registered Toolkits (for Collapsing manager)
    internal readonly HashSet<string> _explicitlyRegisteredToolkits = new(StringComparer.OrdinalIgnoreCase);
    internal readonly List<Middleware.IAgentMiddleware> _middlewares = new(); // Unified middleware list
    internal readonly HPD.Agent.Permissions.PermissionOverrideRegistry _permissionOverrides = new(); // Permission overrides

    // Logging configuration - stored here and applied LAST in RegisterAutoMiddleware
    private LoggingMiddlewareOptions? _loggingOptions = null;

    // Function Collapse tracking for middleware Collapsing
    internal readonly Dictionary<string, string> _functionToToolkitMap = new(); // functionName -> toolTypeName
    internal readonly Dictionary<string, string> _functionToSkillMap = new(); // functionName -> skillName

    // Internal observers for agent-level observability (developer-only, hidden from users)
    private readonly List<IAgentEventObserver> _observers = new();

    // Event handlers for synchronous, ordered event processing (UI, console, web streams)
    private readonly List<IAgentEventHandler> _eventHandlers = new();

    internal readonly Dictionary<Type, object> _providerConfigs = new();
    internal IServiceProvider? _serviceProvider;
    internal ILoggerFactory? _logger;

    // MCP runtime fields (stored as object to avoid circular reference to HPD-Agent.MCP)
    internal object? _mcpClientManager;

    // AIContextProvider factory (protocol-specific, stored as object for extensibility)
    internal object? _contextProviderFactory;

    // Text extraction utility for document processing (shared instance)
    public HPD.Agent.TextExtraction.TextExtractionUtility? _textExtractor;

    //     
    // AOT-COMPATIBLE Toolkit REGISTRY (Phase: AOT Toolkit Registry Hybrid)
    //     
    // These fields enable reflection-free Toolkit instantiation in hot paths.
    // The source generator creates a ToolRegistry.All array with direct delegates.

    /// <summary>
    /// Toolkit catalog loaded from generated ToolkitRegistry.All.
    /// Starts with the calling assembly's registry and lazily loads additional assemblies
    /// when Toolkits from other assemblies are requested via WithToolkit&lt;T&gt;().
    /// </summary>
    internal readonly Dictionary<string, ToolkitFactory> _availableToolkits = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Tracks which assemblies have already been scanned for Toolkit registries.
    /// Used to avoid repeated reflection calls for the same assembly.
    /// </summary>
    internal readonly HashSet<Assembly> _loadedAssemblies = new();

    // Secret resolution
    private ISecretResolver? _secretResolver;
    private readonly List<ISecretResolver> _additionalResolvers = new();

    /// <summary>
    /// Selected Toolkits for this agent (from WithToolkit calls).
    /// Only Toolkits in this list will have their functions created during Build().
    /// </summary>
    internal readonly List<ToolkitFactory> _selectedToolkitFactories = new();

    /// <summary>
    /// Toolkit overrides from builder calls (takes precedence over config).
    /// Maps toolkit name -> ToolkitReference with updated config/metadata.
    /// Used for Config = Base, Builder = Override/Extend pattern.
    /// </summary>
    internal readonly Dictionary<string, ToolkitReference> _toolkitOverrides = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Tracks which toolkits were added via builder (not config).
    /// Used to determine what's an override vs extension.
    /// </summary>
    internal readonly HashSet<string> _builderAddedToolkits = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Builder-time DI middleware instances for toolkit-scoped middleware .
    /// Maps toolkit name -> list of middleware instances supplied via
    /// <c>WithToolkit&lt;T&gt;(opts => opts.AddScopedMiddleware(...))</c>.
    /// Merged with attribute-declared factory middlewares at container expansion time.
    /// </summary>
    internal readonly Dictionary<string, List<Middleware.IAgentMiddleware>> _toolkitScopedMiddlewares
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
    /// Maps Toolkit name -> array of function names to include.
    /// When a Toolkit is auto-registered as a skill dependency, only these functions are included.
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
    // These fields enable cross-assembly middleware state discovery following the ToolkitRegistry pattern.
    // Each assembly generates a MiddlewareStateRegistry.All array with factories for [MiddlewareState] types.

    /// <summary>
    /// Middleware state catalog loaded from generated MiddlewareStateRegistry.All.
    /// Starts with the calling assembly's registry and lazily loads additional assemblies
    /// when toolkits from other assemblies are registered via WithToolkit&lt;T&gt;().
    /// </summary>
    internal readonly Dictionary<string, MiddlewareStateFactory> _stateFactories = new(StringComparer.Ordinal);

    /// <summary>
    /// Tracks which assemblies have already been scanned for state registries.
    /// Used to avoid repeated reflection calls for the same assembly.
    /// </summary>
    internal readonly HashSet<Assembly> _loadedStateAssemblies = new();

    /// <summary>
    /// Toolkit configs from config Toolkits list.
    /// Maps toolkit name -> JsonElement config for CreateFromConfig delegate.
    /// </summary>
    internal readonly Dictionary<string, System.Text.Json.JsonElement> _toolkitConfigs = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Per-toolkit middleware config overrides from <c>ToolkitReference.MiddlewareConfigs</c> .
    /// Maps toolkit name -> (middleware simple type name -> JsonElement config).
    /// Passed to <see cref="ContainerMiddleware"/> at build time for config-constructor middleware instantiation.
    /// </summary>
    internal readonly Dictionary<string, Dictionary<string, System.Text.Json.JsonElement>> _toolkitMiddlewareConfigs
        = new(StringComparer.OrdinalIgnoreCase);

    // ==========  OpenAPI Support  ==========

    /// <summary>
    /// Static singleton set by OpenApiAutoDiscovery.Initialize() via [ModuleInitializer].
    /// Null until HPD-Agent.OpenApi is loaded. Same pattern as provider modules.
    /// </summary>
    private static IOpenApiLoader? s_openApiLoader;

    /// <summary>
    /// OpenAPI sources registered via WithOpenApi() or [OpenApi] toolkit attribute.
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
    /// Adds a pending OpenAPI source registration. Called by WithOpenApi() and
    /// CreateFunctionsFromCatalog() when a toolkit has [OpenApi] methods.
    /// </summary>
    internal void AddOpenApiSource(OpenApiSourceRegistration registration)
    {
        _openApiSources.Add(registration);
    }

    /// <summary>
    /// Creates a new builder with default configuration.
    /// Provider assemblies are automatically discovered via ProviderAutoDiscovery ModuleInitializer.
    /// </summary>
#pragma warning disable IL2026
    public AgentBuilder()
    {
        // Capture calling assembly FIRST, before any method calls
        // GetCallingAssembly() returns the immediate caller, not transitive
        var callingAssembly = Assembly.GetCallingAssembly();

        _config = new AgentConfig();
        _providerRegistry = new ProviderRegistry();

        // Load from HPD-Agent assembly first (ensures core middleware states are always available)
        LoadToolRegistryFromAssembly(typeof(Agent).Assembly);

        // Then load from calling assembly (user-defined toolkits/states)
        LoadToolRegistryFromAssembly(callingAssembly);
        RegisterDiscoveredProviders();
    }
#pragma warning restore IL2026

    /// <summary>
    /// Creates a builder from existing configuration.
    /// Provider assemblies are automatically discovered via ProviderAutoDiscovery ModuleInitializer.
    /// </summary>
#pragma warning disable IL2026
    public AgentBuilder(AgentConfig config)
    {
        // Capture calling assembly FIRST, before any method calls
        var callingAssembly = Assembly.GetCallingAssembly();

        _config = config ?? throw new ArgumentNullException(nameof(config));
        _providerRegistry = new ProviderRegistry();

        // Load from HPD-Agent assembly first (ensures core middleware states are always available)
        LoadToolRegistryFromAssembly(typeof(Agent).Assembly);

        // Then load from calling assembly (user-defined toolkits/states)
        LoadToolRegistryFromAssembly(callingAssembly);
        RegisterDiscoveredProviders();
    }
#pragma warning restore IL2026

    /// <summary>
    /// Creates a builder with custom provider registry (for testing).
    /// Optionally accepts an assembly hint for Toolkit registry discovery.
    /// </summary>
#pragma warning disable IL2026
    public AgentBuilder(AgentConfig config, IProviderRegistry providerRegistry)
    {
        // Capture calling assembly FIRST
        var callingAssembly = Assembly.GetCallingAssembly();

        _config = config;
        _providerRegistry = providerRegistry;

        // Load from HPD-Agent assembly first (ensures core middleware states are always available)
        LoadToolRegistryFromAssembly(typeof(Agent).Assembly);

        // Then load from calling assembly (user-defined toolkits/states)
        LoadToolRegistryFromAssembly(callingAssembly);
    }
#pragma warning restore IL2026

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
            "HPD-Agent.Providers.AzureAI",
            "HPD-Agent.Providers.AzureAIInference",
            "HPD-Agent.Providers.OpenAI",
            "HPD-Agent.Providers.Ollama",
            "HPD-Agent.Providers.GoogleAI",
            "HPD-Agent.Providers.HuggingFace",
            "HPD-Agent.Providers.Bedrock",
            "HPD-Agent.Providers.Mistral",
            "HPD-Agent.Providers.OnnxRuntime"
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
    /// Loads Toolkits, Middlewares, and Middleware States from the generated registries in the specified assembly
    /// and merges them into the _availableToolkits, _availableMiddlewares, and _stateFactories dictionaries.
    /// Uses minimal reflection (GetType calls per assembly) to discover the catalogs.
    /// Thread-safe: tracks loaded assemblies to avoid duplicate processing.
    /// WARNING: Requires source-generated registry types to be preserved in AOT.
    /// </summary>
    /// <param name="assembly">The assembly to search for the generated registries</param>
    [RequiresUnreferencedCode("Registry lookup via Assembly.GetType requires ToolkitRegistry, MiddlewareRegistry, and MiddlewareStateRegistry types to be preserved during AOT compilation.")]
    internal void LoadToolRegistryFromAssembly(Assembly assembly)
    {
        // Skip if already loaded
        if (!_loadedAssemblies.Add(assembly))
        {
            return;
        }

        // Load Toolkit registry
        LoadToolkitRegistryFromAssembly(assembly);

        // Load Middleware registry
        LoadMiddlewareRegistryFromAssembly(assembly);

        // Load Middleware State registry (cross-assembly state discovery)
        LoadStateRegistryFromAssembly(assembly);
    }

    /// <summary>
    /// Loads Toolkits from the generated ToolkitRegistry.All in the specified assembly.
    /// </summary>
    [RequiresUnreferencedCode("Toolkit registry lookup via Assembly.GetType requires ToolkitRegistry type to be preserved during AOT compilation.")]
    private void LoadToolkitRegistryFromAssembly(Assembly assembly)
    {
        try
        {
            // ONE reflection call: Look for generated registry in the specified assembly
            // This type name is a constant known at compile time, making it AOT-safe
            var registryType = assembly.GetType("HPD.Agent.Generated.ToolkitRegistry");

            if (registryType == null)
            {
                // No registry found - no Toolkits available from this assembly
                return;
            }

            // Get the All field (static readonly array - NOT a property!)
            var allField = registryType.GetField("All", BindingFlags.Public | BindingFlags.Static);
            if (allField == null)
            {
                return;
            }

            // Get the ToolkitFactory array
            var factories = allField.GetValue(null) as ToolkitFactory[];
            if (factories == null || factories.Length == 0)
            {
                return;
            }

            // Add to dictionary (new Toolkits from this assembly)
            foreach (var factory in factories)
            {
                // Use TryAdd to avoid overwriting if Toolkit with same name already exists
                _availableToolkits.TryAdd(factory.Name, factory);
            }
        }
        catch (Exception ex)
        {
            // Log warning but don't crash - no Toolkits from this assembly
            _logger?.CreateLogger<AgentBuilder>()
                .LogWarning(ex, "Failed to load ToolkitRegistry.All from assembly {Assembly}", assembly.FullName);
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
    /// This enables cross-assembly state discovery following the ToolkitRegistry pattern.
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
    /// Creates AIFunctions from selected Toolkits using the catalog (zero reflection in hot path).
    /// Also handles instance-based registrations for DI Toolkits.
    /// Phase 4.5: Applies function filters for selective registration.
    /// </summary>
    /// <returns>List of AIFunctions from all selected Toolkits</returns>
    [RequiresUnreferencedCode("Instance-based registrations for DI Toolkits use reflection.")]
    private List<AIFunction> CreateFunctionsFromCatalog()
    {
        var allFunctions = new List<AIFunction>();

        // Process catalog-based Toolkits (zero reflection in hot path)
        foreach (var factory in _selectedToolkitFactories)
        {
            try
            {
                _toolkitContexts.TryGetValue(factory.Name, out var ctx);

                // Create Toolkit instance using AOT-safe resolution:
                // 1. Try DI first (if ServiceProvider available)
                // 2. Try config-based instantiation (if config provided)
                // 3. Try ISecretResolver-only constructor (auto-injected from builder)
                // 4. Fall back to parameterless constructor
                object instance;

                // 1. Try DI first
                if (_serviceProvider != null)
                {
                    var diInstance = _serviceProvider.GetService(factory.ToolkitType);
                    if (diInstance != null)
                    {
                        instance = diInstance;
                        goto HaveInstance;
                    }
                }

                // 2. Try config-based instantiation
                if (_toolkitConfigs.TryGetValue(factory.Name, out var config) && factory.CreateFromConfig != null)
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
                // Collect OpenAPI sources from [OpenApi] toolkit methods (ZERO REFLECTION!)
                // Config is stored as object; cast to OpenApiConfig happens in OpenApiLoader.
                // CollapseWithinToolkit placeholder is false — loader reads it from config directly.
                if (factory.CollectOpenApiSources != null)
                {
                    factory.CollectOpenApiSources(instance, (name, config, parentContainer) =>
                    {
                        _openApiSources.Add(new OpenApiSourceRegistration(
                            Name: name,
                            ParentContainer: parentContainer,
                            CollapseWithinToolkit: false,   // placeholder — loader reads from config
                            Config: config));
                    });
                }

                // Call CreateFunctions delegate (ZERO REFLECTION!)
                var functions = factory.CreateFunctions(instance, ctx ?? _defaulTMetadata);

                // Phase 4.5: Apply function filter if this Toolkit has selective registration
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
                    .LogWarning(ex, "Failed to create functions for Toolkit {ToolkitName}", factory.Name);
            }
        }

        // Process instance-based registrations (for DI Toolkits like AgentPlanToolkit, DynamicMemoryToolkit)
        foreach (var registration in _instanceRegistrations)
        {
            try
            {
                _toolkitContexts.TryGetValue(registration.ToolTypeName, out var ctx);
                var functions = CreateFunctionsFromInstance(registration, ctx ?? _defaulTMetadata);

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
                    .LogWarning(ex, "Failed to create functions for instance Toolkit {ToolkitName}", registration.ToolTypeName);
            }
        }

        return allFunctions;
    }

    /// <summary>
    /// Creates AIFunctions from an instance-based Toolkit registration.
    /// Uses the generated Registration class to create functions from the provided instance.
    /// This is used for DI Toolkits that cannot be instantiated via the catalog.
    /// </summary>
    [RequiresUnreferencedCode("Uses reflection to call generated Registration class for DI Toolkits.")]
    private List<AIFunction> CreateFunctionsFromInstance(ToolInstanceRegistration registration, IToolMetadata? context)
    {
        var instance = registration.Instance;
        var instanceType = instance.GetType();

        // Look up the generated Registration class
        var registrationTypeName = $"{registration.ToolTypeName}Registration";
        var registrationType = instanceType.Assembly.GetType(registrationTypeName);

        if (registrationType == null)
        {
            throw new InvalidOperationException(
                $"Generated registration class {registrationTypeName} not found for DI Toolkit. " +
                $"Ensure the Toolkit has [Function] or [Skill] attributes and the source generator ran successfully.");
        }

        var createToolkitMethod = registrationType.GetMethod("CreateToolkit", BindingFlags.Public | BindingFlags.Static);
        if (createToolkitMethod == null)
        {
            throw new InvalidOperationException(
                $"CreateToolkit method not found in {registrationTypeName}.");
        }

        // Invoke CreateToolkit with the instance
        var parameters = createToolkitMethod.GetParameters();
        object? result;

        if (parameters.Length == 1)
        {
            // Skill-only container: CreateToolkit(IToolMetadata? context)
            result = createToolkitMethod.Invoke(null, new object?[] { context });
        }
        else
        {
            // Regular Toolkit: CreateToolkit(TToolkit instance, IToolMetadata? context)
            result = createToolkitMethod.Invoke(null, new object?[] { instance, context });
        }

        return result as List<AIFunction> ?? new List<AIFunction>();
    }

    /// <summary>
    /// Resolves toolkits from config Toolkits list and adds them to _selectedToolkitFactories.
    /// Implements Config = Base, Builder = Override/Extend pattern:
    /// - Config toolkits are registered first (in order)
    /// - Builder calls can override (replace same name) or extend (add new)
    /// - DI-first resolution: try ServiceProvider first, fall back to CreateInstance
    /// </summary>
    [RequiresUnreferencedCode("Config toolkit resolution may load assemblies dynamically.")]
    private void ResolveConfigToolkits()
    {
        if (_config.Toolkits == null || _config.Toolkits.Count == 0)
            return;

        var logger = _logger?.CreateLogger<AgentBuilder>();
        logger?.LogDebug("Resolving {Count} toolkits from config", _config.Toolkits.Count);

        foreach (var toolkitRef in _config.Toolkits)
        {
            // Check if builder has an override for this toolkit
            var effectiveRef = _toolkitOverrides.TryGetValue(toolkitRef.Name, out var ovr)
                ? ovr
                : toolkitRef;

            // Skip if already added via builder (it will be in _selectedToolkitFactories already)
            if (_builderAddedToolkits.Contains(effectiveRef.Name))
            {
                logger?.LogDebug("Toolkit '{Name}' already registered via builder, skipping config", effectiveRef.Name);
                continue;
            }

            // Look up in available toolkits
            if (!_availableToolkits.TryGetValue(effectiveRef.Name, out var factory))
            {
                logger?.LogWarning(
                    "Toolkit '{Name}' referenced in config not found in registry. " +
                    "Ensure the class has [AIFunction], [Skill], or [SubAgent] methods and a parameterless constructor.",
                    effectiveRef.Name);
                continue;
            }

            // Check if already selected (avoid duplicates)
            if (_selectedToolkitFactories.Any(f => f.Name.Equals(factory.Name, StringComparison.OrdinalIgnoreCase)))
            {
                logger?.LogDebug("Toolkit '{Name}' already selected, skipping duplicate", effectiveRef.Name);
                continue;
            }

            // Add to selected factories
            _selectedToolkitFactories.Add(factory);
            _explicitlyRegisteredToolkits.Add(factory.Name);

            // Handle function filtering from config
            if (effectiveRef.Functions != null && effectiveRef.Functions.Count > 0)
            {
                _toolFunctionFilters[factory.Name] = effectiveRef.Functions.ToArray();
            }

            // Handle config-based instantiation (store config for CreateFunctionsFromCatalog)
            if (effectiveRef.Config.HasValue && factory.CreateFromConfig != null)
            {
                _toolkitConfigs[factory.Name] = effectiveRef.Config.Value;
            }

            // Handle metadata from config
            if (effectiveRef.Metadata.HasValue && factory.MetadataType != null)
            {
                try
                {
                    var metadata = (IToolMetadata?)JsonSerializer.Deserialize(
                        effectiveRef.Metadata.Value.GetRawText(),
                        factory.MetadataType);
                    if (metadata != null)
                    {
                        _toolkitContexts[factory.Name] = metadata;
                    }
                }
                catch (JsonException ex)
                {
                    logger?.LogWarning(ex,
                        "Failed to deserialize metadata for toolkit '{Name}' to type {MetadataType}",
                        effectiveRef.Name, factory.MetadataType.Name);
                }
            }

            // Handle middleware configs from config (§5A)
            if (effectiveRef.MiddlewareConfigs != null && effectiveRef.MiddlewareConfigs.Count > 0
                && factory.CollapseMiddlewareConfigFactories != null)
            {
                _toolkitMiddlewareConfigs[factory.Name] = effectiveRef.MiddlewareConfigs;
            }

            logger?.LogDebug("Resolved toolkit '{Name}' from config", effectiveRef.Name);
        }
    }

    /// <summary>
    /// Registers a toolkit override from builder.
    /// Called by WithToolkit extension methods when using config + builder pattern.
    /// </summary>
    public AgentBuilder WithToolkitOverride(ToolkitReference reference)
    {
        _toolkitOverrides[reference.Name] = reference;
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

    // Legacy reflection-based method removed - now using AOT-compatible MiddlewareRegistry
    // The old ResolveMiddlewareType method used AppDomain.GetAssemblies() and Activator.CreateInstance
    // which are not Native AOT compatible.

    #pragma warning disable CS0618 // Preserve for backward compatibility if needed
    /// <summary>
    /// [DEPRECATED] Resolves a middleware type by name using reflection.
    /// Use ResolveMiddlewareFactory instead for AOT-compatible resolution.
    /// Only used as fallback for middlewares not in the source-generated registry.
    /// </summary>
    [Obsolete("Use ResolveMiddlewareFactory for AOT-compatible resolution. This method uses reflection.")]
    [RequiresUnreferencedCode("Type lookup by name uses reflection.")]
    private Type? ResolveMiddlewareType(string name)
    {
        // Check registry first (AOT-safe)
        if (_availableMiddlewares.TryGetValue(name, out var factory))
            return factory.MiddlewareType;

        // Legacy fallback: Try scanning loaded assemblies
        foreach (var assembly in _loadedAssemblies)
        {
            try
            {
                var type = assembly.GetTypes()
                    .FirstOrDefault(t =>
                        typeof(IAgentMiddleware).IsAssignableFrom(t) &&
                        !t.IsAbstract &&
                        (t.Name == name || t.Name == $"{name}Middleware"));

                if (type != null)
                    return type;
            }
            catch
            {
                // Ignore assemblies that can't be reflected
            }
        }

        return null;
    }

    /// <summary>
    /// Creates a new builder from a JSON configuration file.
    /// </summary>
    /// <param name="jsonFilePath">Path to the JSON file containing AgentConfig data.</param>
    /// <exception cref="ArgumentException">Thrown when the file path is null or empty.</exception>
    /// <exception cref="FileNotFoundException">Thrown when the specified file does not exist.</exception>
    /// <exception cref="JsonException">Thrown when the JSON is invalid or cannot be deserialized.</exception>
    [System.Diagnostics.CodeAnalysis.RequiresUnreferencedCode("Provider assembly loading uses reflection in non-AOT scenarios")]
    public AgentBuilder(string jsonFilePath)
    {
        // Capture calling assembly FIRST, before any method calls
        var callingAssembly = Assembly.GetCallingAssembly();

        if (string.IsNullOrWhiteSpace(jsonFilePath))
            throw new ArgumentException("JSON file path cannot be null or empty.", nameof(jsonFilePath));

        if (!File.Exists(jsonFilePath))
            throw new FileNotFoundException($"Configuration file not found: {jsonFilePath}");

        _providerRegistry = new ProviderRegistry();
        LoadToolRegistryFromAssembly(callingAssembly);

        try
        {
            var jsonContent = File.ReadAllText(jsonFilePath);
            _config = JsonSerializer.Deserialize<AgentConfig>(jsonContent, HPDJsonContext.Default.AgentConfig)
                ?? throw new JsonException("Failed to deserialize AgentConfig from JSON - result was null.");

            RegisterDiscoveredProviders();
        }
        catch (JsonException)
        {
            throw; // Re-throw JSON exceptions as-is
        }
        catch (Exception ex)
        {
            throw new JsonException($"Failed to load or parse configuration file '{jsonFilePath}': {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Creates a new builder from a JSON configuration file.
    /// </summary>
    /// <param name="jsonFilePath">Path to the JSON file containing AgentConfig data.</param>
    /// <returns>A new AgentBuilder instance configured from the JSON file.</returns>
    /// <exception cref="ArgumentException">Thrown when the file path is null or empty.</exception>
    /// <exception cref="FileNotFoundException">Thrown when the specified file does not exist.</exception>
    /// <exception cref="JsonException">Thrown when the JSON is invalid or cannot be deserialized.</exception>
    [System.Diagnostics.CodeAnalysis.RequiresUnreferencedCode("Provider assembly loading uses reflection in non-AOT scenarios")]
    public static AgentBuilder FromJsonFile(string jsonFilePath)
    {
        return new AgentBuilder(jsonFilePath);
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
    // V3: Unified Content Store
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Configure a custom content store for this agent.
    /// The store provides unified storage for skills, knowledge, memory, uploads, and artifacts.
    /// Use <see cref="UseDefaultContentStore"/> for automatic default folder setup.
    /// </summary>
    public AgentBuilder WithContentStore(IContentStore store)
    {
        _contentStore = store ?? throw new ArgumentNullException(nameof(store));
        return this;
    }

    /// <summary>
    /// Configure the content store with default folders (/skills, /knowledge, /memory)
    /// and auto-register FolderDiscoveryMiddleware and ContentStoreToolkit.
    /// This is the recommended one-liner for enabling the V3 content store system.
    /// </summary>
    /// <param name="store">
    /// Optional custom store. Defaults to InMemoryContentStore if not provided.
    /// For production use, pass a LocalFileContentStore or custom implementation.
    /// </param>
    public AgentBuilder UseDefaultContentStore(IContentStore? store = null)
    {
        _contentStore = store ?? new InMemoryContentStore();

        // Register default folders
        _contentStore.CreateFolder("skills", new FolderOptions
        {
            Permissions = ContentPermissions.Read,
            Description = "Agent skill instructions and documentation"
        });
        _contentStore.CreateFolder("knowledge", new FolderOptions
        {
            Permissions = ContentPermissions.Read,
            Description = "Agent knowledge base and expertise"
        });
        _contentStore.CreateFolder("memory", new FolderOptions
        {
            Permissions = ContentPermissions.ReadWrite,
            Description = "Agent working memory and context"
        });

        // Auto-register FolderDiscoveryMiddleware and ContentStoreToolkit
        var toolkit = new ContentStoreToolkit(_contentStore, AgentName ?? "agent");
        var discoveryMiddleware = new Middleware.FolderDiscoveryMiddleware(_contentStore, AgentName);
        discoveryMiddleware.SetToolkit(toolkit);

        _middlewares.Add(discoveryMiddleware);
        // FolderDiscoveryMiddleware propagates session ID to the toolkit via SetToolkit link.

        // Register toolkit for AI function exposure
        _instanceRegistrations.Add(new ToolInstanceRegistration(toolkit, nameof(ContentStoreToolkit)));
        _toolkitContexts[nameof(ContentStoreToolkit)] = null;

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
    /// Exposed so toolkits and connectors can resolve secrets.
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
    /// Legacy method - use WithMaxFunctionCallTurns instead
    /// </summary>
    [Obsolete("Use WithMaxFunctionCallTurns instead - this better reflects that we're limiting turns, not individual function calls")]
    public AgentBuilder WithMaxFunctionCalls(int maxFunctionCalls) => WithMaxFunctionCallTurns(maxFunctionCalls);

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
    /// var agent = new AgentBuilder()
    ///     .WithServiceProvider(services)  // ILoggerFactory required
    ///     .WithTelemetry(sourceName: "MyApp.Agent", enableSensitiveData: false)
    ///     .WithOpenAI(apiKey, "gpt-4")
    ///     .Build();
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
        var telemetryObserver = new TelemetryEventObserver(effectiveSourceName);
        _observers.Add(telemetryObserver);

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
        var observer = new TracingObserver(sourceName ?? "HPD.Agent", sanitizerOptions);
        _observers.Add(observer);
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
    /// var agent = new AgentBuilder()
    ///     .WithServiceProvider(services)
    ///     .WithLogging()
    ///     .WithOpenAI(apiKey, "gpt-4")
    ///     .Build();
    ///
    /// // Minimal logging (just function names with timing)
    /// var agent = new AgentBuilder()
    ///     .WithLogging(options: LoggingMiddlewareOptions.Minimal)
    ///     .Build();
    ///
    /// // Verbose logging (everything)
    /// var agent = new AgentBuilder()
    ///     .WithLogging(options: LoggingMiddlewareOptions.Verbose)
    ///     .Build();
    ///
    /// // Custom configuration
    /// var agent = new AgentBuilder()
    ///     .WithLogging(options: new LoggingMiddlewareOptions
    ///     {
    ///         LogFunction = true,
    ///         LogIteration = true,
    ///         IncludeArguments = false,
    ///         MaxStringLength = 500
    ///     })
    ///     .Build();
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
            _observers.Add(loggingObserver);
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
    /// var agent = new AgentBuilder()
    ///     .WithLogging(loggerFactory)
    ///     .WithProvider("openai", "gpt-4", apiKey)
    ///     .Build();
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
    /// Registers a custom event observer to handle agent events.
    /// Observers receive all internal agent events (AgentEvent) and can process them asynchronously.
    /// Use this to implement custom event handling, logging, metrics collection, or UI updates.
    /// </summary>
    /// <param name="observer">The observer to register. Can use <see cref="IEventHandler"/> or <see cref="IAgentEventObserver"/> - both are equivalent.</param>
    /// <returns>The builder for chaining</returns>
    /// <remarks>
    /// <para>
    /// <b>Observer Pattern Benefits:</b>
    /// </para>
    /// <list type="bullet">
    /// <item><description>Reusable event handling logic across multiple agents</description></item>
    /// <item><description>Fire-and-forget async processing (doesn't block agent execution)</description></item>
    /// <item><description>Automatic circuit breaker protection (failing observers auto-disabled after 10 failures)</description></item>
    /// <item><description>Selective filtering via <c>ShouldProcess()</c> method</description></item>
    /// </list>
    /// <para>
    /// <b>Example - Custom Event Handler:</b>
    /// <code>
    /// public class MyEventHandler : IEventHandler
    /// {
    ///     public bool ShouldProcess(AgentEvent evt)
    ///     {
    ///         // Filter events you care about
    ///         return evt is PermissionRequestEvent or TextDeltaEvent;
    ///     }
    ///
    ///     public async Task OnEventAsync(AgentEvent evt, CancellationToken ct)
    ///     {
    ///         switch (evt)
    ///         {
    ///             case PermissionRequestEvent permReq:
    ///                 await HandlePermissionAsync(permReq, ct);
    ///                 break;
    ///             case TextDeltaEvent textDelta:
    ///                 Console.Write(textDelta.Text);
    ///                 break;
    ///         }
    ///     }
    /// }
    ///
    /// // Register with agent
    /// var agent = new AgentBuilder(config)
    ///     .WithObserver(new MyEventHandler())
    ///     .Build();
    /// </code>
    /// </para>
    /// <para>
    /// <b>Multiple Observers:</b> Call <c>WithObserver()</c> multiple times to register multiple observers.
    /// All observers run in parallel via fire-and-forget pattern.
    /// </para>
    /// <para>
    /// <b>Built-in Observers:</b> The framework automatically registers:
    /// - <c>LoggingEventObserver</c> (when you call <c>WithLogging()</c>)
    /// - <c>TelemetryEventObserver</c> (when you call <c>WithTelemetry()</c>)
    /// </para>
    /// </remarks>
    public AgentBuilder WithObserver(IAgentEventObserver observer)
    {
        if (observer == null)
            throw new ArgumentNullException(nameof(observer));

        _observers.Add(observer);
        return this;
    }

    /// <summary>
    /// Registers an event handler for synchronous, ordered event processing.
    /// Use this for UI handlers (console, web streams) that need guaranteed ordering.
    /// </summary>
    /// <param name="handler">The event handler to register</param>
    /// <returns>The builder for chaining</returns>
    /// <remarks>
    /// <para>
    /// Unlike <see cref="WithObserver"/>, event handlers are awaited synchronously
    /// in the event loop, guaranteeing events are processed in order.
    /// </para>
    /// <para>
    /// <b>Use WithEventHandler for:</b>
    /// <list type="bullet">
    /// <item>Console output that needs correct ordering</item>
    /// <item>Web UI streaming (SSE, WebSockets)</item>
    /// <item>Permission prompts that need user interaction</item>
    /// </list>
    /// </para>
    /// <para>
    /// <b>Use WithObserver for:</b>
    /// <list type="bullet">
    /// <item>Telemetry and metrics (ordering doesn't matter)</item>
    /// <item>Background logging</item>
    /// <item>Analytics</item>
    /// </list>
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// var agent = new AgentBuilder(config)
    ///     .WithEventHandler(new ConsoleEventHandler())  // Synchronous, ordered
    ///     .WithObserver(new TelemetryObserver())        // Fire-and-forget
    ///     .Build();
    /// </code>
    /// </example>
    public AgentBuilder WithEventHandler(IAgentEventHandler handler)
    {
        if (handler == null)
            throw new ArgumentNullException(nameof(handler));

        _eventHandlers.Add(handler);
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
    /// var agent = new AgentBuilder()
    ///     .WithServiceProvider(services)  // IDistributedCache required
    ///     .WithCaching(
    ///         cacheExpiration: TimeSpan.FromHours(1),
    ///         cacheStatefulConversations: false)  // Don't cache with ConversationId
    ///     .WithOpenAI(apiKey, "gpt-4")
    ///     .Build();
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
    /// var agent = new AgentBuilder()
    ///     .WithBackgroundResponses(config =>
    ///     {
    ///         config.DefaultAllow = true;
    ///         config.AutoPollToCompletion = true;
    ///         config.DefaultPollingInterval = TimeSpan.FromSeconds(3);
    ///         config.DefaultTimeout = TimeSpan.FromMinutes(10);
    ///     })
    ///     .Build();
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
        _config.DefaultReasoning = new ReasoningOptions { Effort = effort, Output = output };
        return this;
    }

    /// <summary>
    /// Preserves reasoning/thinking content in conversation history across turns.
    /// When true, reasoning blocks are included when sending history back to the provider,
    /// which is required for Anthropic extended thinking to work correctly across turns
    /// (ProtectedData must be round-tripped verbatim).
    /// Default: false (reasoning shown during streaming but excluded from history to save tokens).
    /// </summary>
    public AgentBuilder WithPreserveReasoningInHistory(bool preserve = true)
    {
        _config.PreserveReasoningInHistory = preserve;
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
        _config.ChatClientMiddleware ??= new();
        _config.ChatClientMiddleware.Add(middleware);
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
    /// Sets default chat options (including backend tools)
    /// </summary>
    public AgentBuilder WithDefaultOptions(ChatOptions options)
    {
        if (_config.Provider == null)
            _config.Provider = new ProviderConfig();
        _config.Provider.DefaultChatOptions = options;
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
    /// Internal method - use protocol-specific Build methods (e.g., BuildMicrosoftAgent()).
    /// Validation behavior is controlled by the ValidationConfig (see WithValidation()).
    /// </summary>
    /// <param name="cancellationToken">Cancellation token for async operations</param>
    [RequiresUnreferencedCode("Agent building may use Toolkit registration methods that require reflection.")]
    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "RequiresUnreferencedCode declared on method")]
    /// <summary>
    /// Builds the protocol-agnostic core agent asynchronously.
    /// Required for provider validation (LLM connectivity checks) during initialization.
    /// </summary>
    public async Task<Agent> BuildAsync(CancellationToken cancellationToken = default)
    {
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
        // Users can override with WithSessionStore() for persistent storage (JsonSessionStore, etc.)
        if (_config.SessionStore == null)
        {
            _config.SessionStore = new InMemorySessionStore();
            _logger?.CreateLogger<AgentBuilder>().LogInformation(
                "Using default InMemorySessionStore (in-memory, ephemeral). " +
                "Use .WithSessionStore() for persistence.");
        }
        _config.SessionStoreOptions ??= new SessionStoreOptions();

        // Resolve config middlewares before auto-middleware registration
        // This enables Config = Base, Builder = Override/Extend pattern
        ResolveConfigMiddlewares();

        RegisterAutoMiddleware(buildData);
        return CreateAgent(buildData);
    }

    /// <summary>
    /// Registers all auto-middleware (error handling, history reduction, tool Collapsing, etc).
    /// Called by both sync and async build paths to eliminate code duplication.
    /// </summary>
    private void RegisterAutoMiddleware(AgentBuildDependencies buildData)
    {
        // Set explicitly registered Toolkits in config for Collapsing manager
        _config.explicitlyRegisteredToolkits = _explicitlyRegisteredToolkits
            .ToImmutableHashSet(StringComparer.OrdinalIgnoreCase);

        // Set global config for source-generated code to access (sync path sets this, harmless when called from async)
        AgentConfig.GlobalConfig = _config;

        // Register ContinuationPermissionMiddleware if enabled
        // This requests user permission when iteration limit is reached
        // Only register if we have a reasonable iteration limit set
        if (_config!.MaxAgenticIterations > 0 && _config.MaxAgenticIterations < 1000)
        {
            _middlewares.Add(new ContinuationPermissionMiddleware(
                maxIterations: _config.MaxAgenticIterations,
                extensionAmount: _config.ContinuationExtensionAmount));
        }

        // Register HistoryReductionMiddleware if enabled
        // This reduces conversation history to manage context window size
        if (_config.HistoryReduction?.Enabled == true)
        {
            var chatReducer = CreateChatReducer(buildData.ClientToUse, _config, buildData.SummarizerClient);
            _middlewares.Add(new HistoryReductionMiddleware
            {
                ChatReducer = chatReducer,
                Config = _config.HistoryReduction,
                SystemInstructions = _config.SystemInstructions
            });
        }

        // Register AssetUploadMiddleware with the agent-level content store (if configured).
        // Transforms DataContent → UriContent for efficient storage.
        // Zero-cost when _contentStore is null (no-op middleware).
        _middlewares.Add(new Middleware.AssetUploadMiddleware(_contentStore));

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
                _explicitlyRegisteredToolkits.ToImmutableHashSet(StringComparer.OrdinalIgnoreCase),
                _availableToolkits,           // toolkit factory registry for scoped middleware 
                _toolkitScopedMiddlewares,    // builder-time DI instances 
                _toolkitMiddlewareConfigs,    // config-ctor middleware configs from ToolkitReference 
                _config.Collapsing,
                containerLogger);
            _middlewares.Add(containerMiddleware);

            // NOTE: ContainerErrorRecoveryMiddleware has been consolidated into ContainerMiddleware.
            // The Smart Recovery functionality (hidden items, qualified names) is now integrated
            // directly into ContainerMiddleware's BeforeToolExecutionAsync method.
        }

        // Register ClientToolMiddleware automatically
        // This enables Client-defined tools without explicit configuration.
        // It's a no-op if no Client Toolkits are registered via AgentClientInput.
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
            _functionToToolkitMap,
            _functionToSkillMap,
            _middlewares,
            _serviceProvider,
            _observers,
            _eventHandlers,
            _providerRegistry,
            _stateFactories,
            buildData.OwnedHttpClients);
    }

    /// <summary>
    /// Loads MCP tools from toolkit-owned [MCPServer] methods.
    /// Iterates through selected toolkit factories with HasMCPServers=true,
    /// discovers MCPServerRegistration objects via reflection on the generated Registration class,
    /// resolves configs, and loads tools through MCPClientManager.
    /// </summary>
#pragma warning disable IL2075
    private async Task<List<AIFunction>> LoadToolkitMCPServersAsync(CancellationToken cancellationToken)
    {
        var allTools = new List<AIFunction>();

        // Find toolkits that have MCP servers
        var toolkitsWithMcp = _selectedToolkitFactories.Where(f => f.HasMCPServers).ToList();
        if (toolkitsWithMcp.Count == 0)
            return allTools;

        // Ensure we have an MCP client manager (create one if needed)
        if (McpClientManager == null)
        {
            // Try to create MCPClientManager via reflection (same pattern as WithMCP)
            var mcpManagerType = Type.GetType("HPD.Agent.MCP.MCPClientManager, HPD-Agent.MCP");
            if (mcpManagerType == null)
            {
                _logger?.CreateLogger<AgentBuilder>().LogWarning(
                    "Toolkits have [MCPServer] attributes but HPD-Agent.MCP assembly is not referenced. Skipping MCP server loading.");
                return allTools;
            }

            var logger = _logger?.CreateLogger("HPD.Agent.MCP.MCPClientManager")
                ?? Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance;
            McpClientManager = Activator.CreateInstance(mcpManagerType, logger, null);
        }

        var maxFunctionNames = _config.Collapsing?.MaxFunctionNamesInDescription ?? 10;

        foreach (var factory in toolkitsWithMcp)
        {
            try
            {
                // Find the Registration class: {ToolkitName}Registration
                var registrationType = factory.ToolkitType.Assembly.GetType(
                    $"{factory.Name}Registration") ??
                    factory.ToolkitType.Assembly.GetTypes().FirstOrDefault(t =>
                        t.Name == $"{factory.Name}Registration");

                if (registrationType == null)
                {
                    _logger?.CreateLogger<AgentBuilder>().LogWarning(
                        "Could not find {ToolkitName}Registration class for MCP server loading", factory.Name);
                    continue;
                }

                // Get the MCPServers static property
                var mcpServersProp = registrationType.GetProperty("MCPServers",
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);

                if (mcpServersProp == null)
                {
                    _logger?.CreateLogger<AgentBuilder>().LogWarning(
                        "Registration class {ToolkitName}Registration has no MCPServers property", factory.Name);
                    continue;
                }

                // Get the registrations list
                var registrations = mcpServersProp.GetValue(null);
                if (registrations == null) continue;

                // Get the toolkit instance for instance-method MCP servers
                object? toolkitInstance = null;
                if (_serviceProvider != null)
                {
                    toolkitInstance = _serviceProvider.GetService(factory.ToolkitType);
                }
                if (toolkitInstance == null && factory.CreateWithSecrets != null && _secretResolver != null)
                {
                    toolkitInstance = factory.CreateWithSecrets(_secretResolver);
                }
                toolkitInstance ??= factory.CreateInstance();

                // Iterate over MCPServerRegistration objects
                var registrationList = (System.Collections.IEnumerable)registrations;
                foreach (var regObj in registrationList)
                {
                    // Use reflection to access MCPServerRegistration properties
                    var regType = regObj.GetType();
                    var fromManifest = (string?)regType.GetProperty("FromManifest")?.GetValue(regObj);
                    var manifestServerName = (string?)regType.GetProperty("ManifestServerName")?.GetValue(regObj);
                    var name = (string?)regType.GetProperty("Name")?.GetValue(regObj) ?? string.Empty;
                    var description = (string?)regType.GetProperty("Description")?.GetValue(regObj);
                    var parentToolkit = (string?)regType.GetProperty("ParentToolkit")?.GetValue(regObj) ?? factory.Name;
                    var collapseWithinToolkit = (bool)(regType.GetProperty("CollapseWithinToolkit")?.GetValue(regObj) ?? false);
                    var requiresPermissionOverride = (bool?)regType.GetProperty("RequiresPermissionOverride")?.GetValue(regObj);

                    object? config = null;

                    if (fromManifest != null)
                    {
                        // FromManifest mode: load config from manifest file
                        config = await LoadConfigFromManifestAsync(fromManifest, manifestServerName ?? name, cancellationToken);
                    }
                    else
                    {
                        // Inline config mode: call the provider delegate
                        var staticProvider = regType.GetProperty("StaticConfigProvider")?.GetValue(regObj);
                        var instanceProvider = regType.GetProperty("InstanceConfigProvider")?.GetValue(regObj);

                        if (staticProvider is Delegate staticDel)
                        {
                            config = staticDel.DynamicInvoke();
                        }
                        else if (instanceProvider is Delegate instanceDel)
                        {
                            config = instanceDel.DynamicInvoke(toolkitInstance);
                        }
                    }

                    if (config == null)
                    {
                        _logger?.CreateLogger<AgentBuilder>().LogDebug(
                            "MCP server '{ServerName}' in toolkit '{ToolkitName}' returned null config, skipping",
                            name, factory.Name);
                        continue;
                    }

                    // Set toolkit-awareness properties on the config
                    var configType = config.GetType();
                    configType.GetProperty("ParentToolkit")?.SetValue(config, parentToolkit);
                    configType.GetProperty("CollapseWithinToolkit")?.SetValue(config, collapseWithinToolkit);

                    // Apply attribute overrides
                    if (requiresPermissionOverride.HasValue)
                    {
                        configType.GetProperty("RequiresPermission")?.SetValue(config, requiresPermissionOverride.Value);
                    }

                    if (!string.IsNullOrEmpty(description))
                    {
                        var descProp = configType.GetProperty("Description");
                        var currentDesc = (string?)descProp?.GetValue(config);
                        if (string.IsNullOrEmpty(currentDesc))
                        {
                            descProp?.SetValue(config, description);
                        }
                    }

                    // Load tools via MCPClientManager.LoadToolsForToolkitAsync
                    var mcpManagerType = McpClientManager!.GetType();
                    var loadMethod = mcpManagerType.GetMethod("LoadToolsForToolkitAsync");

                    if (loadMethod != null)
                    {
                        var task = loadMethod.Invoke(McpClientManager, new object[] { config, maxFunctionNames, cancellationToken })
                            as Task<List<AIFunction>>;

                        if (task != null)
                        {
                            var tools = await task;
                            allTools.AddRange(tools);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger?.CreateLogger<AgentBuilder>().LogWarning(ex,
                    "Failed to load MCP servers for toolkit '{ToolkitName}': {Error}",
                    factory.Name, ex.Message);
            }
        }

        return allTools;
    }

    /// <summary>
    /// Loads an MCPServerConfig from a manifest file by server name.
    /// </summary>
    private async Task<object?> LoadConfigFromManifestAsync(string manifestPath, string serverName, CancellationToken cancellationToken)
    {
        try
        {
            if (!File.Exists(manifestPath))
            {
                _logger?.CreateLogger<AgentBuilder>().LogWarning(
                    "MCP manifest file not found: {ManifestPath}", manifestPath);
                return null;
            }

            var json = await File.ReadAllTextAsync(manifestPath, cancellationToken);

            // Use reflection to parse manifest and find server
            var manifestType = Type.GetType("HPD.Agent.MCP.MCPManifest, HPD-Agent.MCP");
            var configType = Type.GetType("HPD.Agent.MCP.MCPServerConfig, HPD-Agent.MCP");
            if (manifestType == null || configType == null) return null;

            var manifest = System.Text.Json.JsonSerializer.Deserialize(json, manifestType);
            if (manifest == null) return null;

            var serversProp = manifestType.GetProperty("Servers");
            var servers = serversProp?.GetValue(manifest) as System.Collections.IEnumerable;
            if (servers == null) return null;

            foreach (var server in servers)
            {
                var nameProp = server.GetType().GetProperty("Name");
                var name = (string?)nameProp?.GetValue(server);
                if (string.Equals(name, serverName, StringComparison.OrdinalIgnoreCase))
                {
                    return server;
                }
            }

            _logger?.CreateLogger<AgentBuilder>().LogWarning(
                "Server '{ServerName}' not found in manifest '{ManifestPath}'", serverName, manifestPath);
            return null;
        }
        catch (Exception ex)
        {
            _logger?.CreateLogger<AgentBuilder>().LogWarning(ex,
                "Failed to load manifest '{ManifestPath}': {Error}", manifestPath, ex.Message);
            return null;
        }
    }
#pragma warning restore IL2075

    /// <summary>
    /// Invokes MCP methods via reflection. This is only called from RequiresUnreferencedCode context,
    /// so the trimmer knows MCPClientManager types must be preserved.
    /// </summary>
#pragma warning disable IL2075
    private async Task<List<AIFunction>> InvokeMCPMethodAsync(
        object mcpManager,
        string methodName,
        string manifestPath,
        int maxFunctionNames,
        CancellationToken cancellationToken)
    {
        var managerType = mcpManager.GetType();
        var method = managerType.GetMethod(
            methodName,
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance,
            null,
            new[] { typeof(string), typeof(bool), typeof(int), typeof(System.Threading.CancellationToken) },
            null);

        if (method == null)
            throw new InvalidOperationException($"MCPClientManager does not have {methodName} method");

        var task = method.Invoke(mcpManager, new object[] { manifestPath, false, maxFunctionNames, cancellationToken })
            as System.Threading.Tasks.Task<List<AIFunction>>;

        if (task == null)
            throw new InvalidOperationException($"MCP method {methodName} returned null or wrong type");

        return await task;
    }
#pragma warning restore IL2075

    /// <summary>
    /// Builds all dependencies needed for agent construction.
    /// WARNING: This method uses reflection for MCP tool loading. It requires HPD.Agent.MCP assembly
    /// and its public types to be preserved during AOT compilation. For Native AOT deployment,
    /// ensure MCPClientManager type is explicitly preserved in your rd.xml or RootDescriptor.
    /// </summary>
    [RequiresUnreferencedCode("MCP tool loading via reflection. Requires HPD.Agent.MCP types preserved.")]
    private async Task<AgentBuildDependencies> BuildDependenciesAsync(CancellationToken cancellationToken)
    {
        // === V3: INITIALIZE SKILL DOCUMENTS VIA CONTENT STORE ===
        // Each toolkit registration class with skill documents generates InitializeDocumentsAsync.
        // Idempotent: same document ID + same content hash = no-op (startup-safe).
        if (_contentStore != null)
        {
            var docLogger = _logger?.CreateLogger<AgentBuilder>();
            foreach (var factory in _selectedToolkitFactories)
            {
                if (factory.InitializeDocumentsAsync != null)
                {
                    docLogger?.LogDebug("Initializing skill documents for toolkit {Toolkit}", factory.Name);
                    await factory.InitializeDocumentsAsync(_contentStore, cancellationToken).ConfigureAwait(false);
                }
            }
        }

        // === TESTING BYPASS: If BaseClient is already set, skip provider resolution ===
        // This allows tests to inject fake clients without configuring a real provider
        if (_baseClient != null)
        {
            // Use generic error handler for testing
            var testErrorHandler = new HPD.Agent.ErrorHandling.GenericErrorHandler();

            return new AgentBuildDependencies(
                _baseClient,
                _config.Provider?.DefaultChatOptions,
                testErrorHandler);
        }

        // === DEFERRED PROVIDER: Skip validation when chat client will be provided at runtime ===
        // Used for agents in multi-agent workflows that inherit the parent's chat client
        if (_deferredProvider)
        {
            // Return null client - will be provided via AgentRunConfig.OverrideChatClient at runtime
            var deferredErrorHandler = new HPD.Agent.ErrorHandling.GenericErrorHandler();
            return new AgentBuildDependencies(
                null!, // Client will be provided at runtime via OverrideChatClient
                _config.Provider?.DefaultChatOptions,
                deferredErrorHandler);
        }

        // === START: VALIDATION LOGIC ===
        AgentConfigValidator.ValidateAndThrow(_config);

        if (_config.Provider == null)
            throw new InvalidOperationException("Provider configuration is required.");

        // ✨ AUTO-CONFIGURE: If no configuration provided, create default configuration
        // Automatically loads from appsettings.json in the application directory
        if (_configuration == null)
        {
            try
            {
                // Use AppContext.BaseDirectory for PublishSingleFile compatibility
                // Falls back to current directory if BaseDirectory is not available
                var basePath = AppContext.BaseDirectory ?? Directory.GetCurrentDirectory();

                _configuration = new ConfigurationBuilder()
                    .SetBasePath(basePath)
                    .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
                    .AddEnvironmentVariables()
                    .Build();
            }
            catch (Exception ex)
            {
                // If auto-configuration fails, we'll continue without it
                // and provide helpful error message later if API key is missing
                Console.WriteLine($"[AgentBuilder] Auto-configuration warning: {ex.Message}");
            }
        }

        // ✨ PRIORITY 1: Try to resolve from Providers section (recommended pattern)
        if (_configuration != null && !string.IsNullOrEmpty(_config.Provider.ProviderKey))
        {
            var providerName = _config.Provider.ProviderKey;
            var providerSection = _configuration.GetSection($"Providers:{providerName}")
                               ?? _configuration.GetSection($"Providers:{Capitalize(providerName)}");

            if (providerSection.Exists())
            {
                // Apply provider section values (only if not already set in code)
                var sectionProviderKey = providerSection["ProviderKey"];
                if (!string.IsNullOrEmpty(sectionProviderKey) && string.IsNullOrEmpty(_config.Provider.ProviderKey))
                    _config.Provider.ProviderKey = sectionProviderKey;

                var sectionApiKey = providerSection["ApiKey"];
                if (!string.IsNullOrEmpty(sectionApiKey) && string.IsNullOrEmpty(_config.Provider.ApiKey))
                    _config.Provider.ApiKey = sectionApiKey;

                var sectionModelName = providerSection["ModelName"];
                if (!string.IsNullOrEmpty(sectionModelName) && string.IsNullOrEmpty(_config.Provider.ModelName))
                    _config.Provider.ModelName = sectionModelName;

                var sectionEndpoint = providerSection["Endpoint"];
                if (!string.IsNullOrEmpty(sectionEndpoint) && string.IsNullOrEmpty(_config.Provider.Endpoint))
                    _config.Provider.Endpoint = sectionEndpoint;
            }
        }

        // ✨ PRIORITY 2: Try to resolve from connection string (backward compatibility)
        if (_configuration != null)
        {
            // Try standard ConnectionStrings section
            var connectionString = _configuration.GetConnectionString("Agent")
                                ?? _configuration.GetConnectionString("ChatClient")
                                ?? _configuration.GetConnectionString("Provider");

            if (!string.IsNullOrEmpty(connectionString) && ProviderConnectionInfo.TryParse(connectionString, out var connInfo))
            {
                // Apply connection string values (only if not already set in code)
                if (!string.IsNullOrEmpty(connInfo.Provider) && string.IsNullOrEmpty(_config.Provider.ProviderKey))
                    _config.Provider.ProviderKey = connInfo.Provider;

                if (!string.IsNullOrEmpty(connInfo.AccessKey) && string.IsNullOrEmpty(_config.Provider.ApiKey))
                    _config.Provider.ApiKey = connInfo.AccessKey;

                if (!string.IsNullOrEmpty(connInfo.Model) && string.IsNullOrEmpty(_config.Provider.ModelName))
                    _config.Provider.ModelName = connInfo.Model;

                if (connInfo.Endpoint != null && string.IsNullOrEmpty(_config.Provider.Endpoint))
                    _config.Provider.Endpoint = connInfo.Endpoint.ToString();
            }
        }

        // ✨ PRIORITY 3: Resolve from individual configuration keys (backward compatibility)
        if (string.IsNullOrEmpty(_config.Provider.ApiKey))
        {
            var providerKeyForConfig = _config.Provider.ProviderKey;
            if (string.IsNullOrEmpty(providerKeyForConfig))
                providerKeyForConfig = "openai"; // fallback default

            var apiKey = await _secretResolver!.ResolveOrDefaultAsync(
                $"{providerKeyForConfig}:ApiKey",
                _config.Provider.ApiKey,
                cancellationToken);

            if (!string.IsNullOrEmpty(apiKey))
                _config.Provider.ApiKey = apiKey;
        }

        // Also try to resolve endpoint if not set
        if (string.IsNullOrEmpty(_config.Provider.Endpoint))
        {
            var providerKeyForConfig = _config.Provider.ProviderKey;
            if (string.IsNullOrEmpty(providerKeyForConfig))
                providerKeyForConfig = "openai"; // fallback default

            var endpoint = await _secretResolver!.ResolveOrDefaultAsync(
                $"{providerKeyForConfig}:Endpoint",
                _config.Provider.Endpoint,
                cancellationToken);

            if (!string.IsNullOrEmpty(endpoint))
                _config.Provider.Endpoint = endpoint;
        }

        // Resolve provider from registry
        var providerKey = _config.Provider.ProviderKey;
        if (string.IsNullOrEmpty(providerKey))
            throw new InvalidOperationException("ProviderKey in ProviderConfig cannot be empty.");

        var providerFeatures = _providerRegistry.GetProvider(providerKey);

        if (providerFeatures == null)
        {
            var availableProviders = string.Join(", ", _providerRegistry.GetRegisteredProviders());
            throw new InvalidOperationException(
                $"Provider '{providerKey}' not registered. " +
                $"Available providers: [{availableProviders}]. " +
                $"Did you forget to reference the HPD-Agent.Providers.{Capitalize(providerKey)} package?");
        }

        // Validate provider-specific configuration
        ProviderValidationResult validation;

        // Check if async validation is enabled in configuration
        var enableAsyncValidation = _config.Validation?.EnableAsyncValidation ?? false;

        // Try async validation first if enabled and supported
        if (enableAsyncValidation)
        {
            var asyncValidationTask = providerFeatures.ValidateConfigurationAsync(_config.Provider, cancellationToken);

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
                    validation = providerFeatures.ValidateConfiguration(_config.Provider);
                }
            }
            else
            {
                // Provider doesn't support async validation (returns null Task), use sync
                validation = providerFeatures.ValidateConfiguration(_config.Provider);
            }
        }
        else
        {
            // Async validation disabled, use sync only
            validation = providerFeatures.ValidateConfiguration(_config.Provider);
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
                var providerCapitalized = Capitalize(providerKey);

                errorMessage += $"\n\n Configure your API key using any of these methods:\n\n" +
                    $" PROVIDERS SECTION (recommended for multiple providers):\n" +
                    $"   appsettings.json → \"Providers\": {{\n" +
                    $"     \"{providerCapitalized}\": {{\n" +
                    $"       \"ProviderKey\": \"{providerKey}\",\n" +
                    $"       \"ModelName\": \"your-model\",\n" +
                    $"       \"ApiKey\": \"your-api-key\"\n" +
                    $"     }}\n" +
                    $"   }}\n\n" +
                    $" CONNECTION STRING (legacy, single provider):\n" +
                    $"   appsettings.json → \"ConnectionStrings\": {{\n" +
                    $"     \"Agent\": \"Provider={providerKey};AccessKey=your-api-key;Model=your-model\"\n" +
                    $"   }}\n\n" +
                    $"  ENVIRONMENT VARIABLE:\n" +
                    $"   {providerUpper}_API_KEY=your-api-key\n\n" +
                    $" USER SECRETS (development only):\n" +
                    $"   dotnet user-secrets set \"Providers:{providerCapitalized}:ApiKey\" \"your-api-key\"\n\n" +
                    $" CODE (for testing only, not recommended):\n" +
                    $"   Provider = new ProviderConfig {{ ApiKey = \"your-api-key\", ... }}";
            }            throw new InvalidOperationException(errorMessage);
        }

        // Create chat client and error handler via provider factories
        // Skip client creation if WithChatClient() was used (e.g., SubAgent inheriting parent's client)
        if (_baseClient == null)
        {
            _baseClient = providerFeatures.CreateChatClient(_config.Provider, _serviceProvider);

            if (_baseClient == null)
                throw new InvalidOperationException($"The factory for provider '{providerKey}' returned a null chat client.");
        }

        // Note: Error handler is now created in the middleware registration phase above,
        // not here. This ensures it's only created if retry is actually enabled.

        // Use base client directly (no middleware pipeline)
        // Observability (telemetry, logging, caching) is integrated directly into Agent.cs
        var clientToUse = _baseClient;

        // Dynamic Memory registration is handled by WithDynamicMemory() extension method
        // No need to register here in Build() - the extension already adds Middleware and Toolkit

        //
        // RESOLVE CONFIG TOOLKITS (Phase: Config Serialization)
        //
        // Resolve toolkits from config before creating functions.
        // This enables the Config = Base, Builder = Override/Extend pattern.
        ResolveConfigToolkits();

        //
        // CREATE Toolkit FUNCTIONS (AOT-Compatible - Zero Reflection in Hot Path)
        //
        // All Toolkits are registered via the catalog (ToolRegistry.All) using direct delegate calls.
        // Instance-based Toolkits (requiring DI) use their own direct delegate calls.
        // No reflection fallback - the catalog is required.

        var toolFunctions = CreateFunctionsFromCatalog();

        // Middleware out container functions if Collapsing is disabled
        // Container functions are only needed when Collapsing is enabled for the two-turn expansion flow
        if (_config.Collapsing?.Enabled != true)
        {
            toolFunctions = toolFunctions.Where(f =>
                !(f.AdditionalProperties?.TryGetValue("IsContainer", out var isContainer) == true &&
                  isContainer is bool isCont && isCont)
            ).ToList();
        }

        // Load MCP tools if configured
        if (McpClientManager != null)
        {
            try
            {
                List<AIFunction> mcpTools;
                if (_config.Mcp != null && !string.IsNullOrEmpty(_config.Mcp.ManifestPath))
                {
                    // Get max function names configuration (no longer using global MCP Collapsing - that's per-server now)
                    var maxFunctionNames = _config.Collapsing?.MaxFunctionNamesInDescription ?? 10;

                    // Check if this is actually content vs path based on if it starts with '{'
                    if (_config.Mcp.ManifestPath.TrimStart().StartsWith("{"))
                    {
                        // AOT-safe reflection-based method invocation (no dynamic required)
                        mcpTools = await InvokeMCPMethodAsync(
                            McpClientManager,
                            "LoadToolsFromManifestContentAsync",
                            _config.Mcp.ManifestPath,
                            maxFunctionNames,
                            cancellationToken);
                    }
                    else
                    {
                        // AOT-safe reflection-based method invocation (no dynamic required)
                        mcpTools = await InvokeMCPMethodAsync(
                            McpClientManager,
                            "LoadToolsFromManifestAsync",
                            _config.Mcp.ManifestPath,
                            maxFunctionNames,
                            cancellationToken);
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

        // Load toolkit-owned MCP servers (from [MCPServer] attributes)
        var toolkitMcpTools = await LoadToolkitMCPServersAsync(cancellationToken);
        if (toolkitMcpTools.Count > 0)
        {
            toolFunctions.AddRange(toolkitMcpTools);
            _logger?.CreateLogger<AgentBuilder>().LogInformation("Successfully integrated {Count} toolkit-owned MCP tools into agent", toolkitMcpTools.Count);
        }

        // Note: Old SkillDefinition-based skills have been removed in favor of type-safe Skill class.
        // Skills are now registered via Toolkits and auto-discovered by the source generator.

        // Load OpenAPI sources (from WithOpenApi() or [OpenApi] toolkit attributes)
        OpenApiLoadResult? openApiResult = null;
        if (_openApiSources.Count > 0)
        {
            if (s_openApiLoader == null)
                throw new InvalidOperationException(
                    "OpenAPI sources were registered but HPD-Agent.OpenApi is not loaded. " +
                    "Add a reference to HPD-Agent.OpenApi.");

            // The loader owns all config interpretation, HttpClient creation, and function generation.
            // It returns any internally-created HttpClients for the Agent to dispose.
            openApiResult = await s_openApiLoader.LoadAllAsync(_openApiSources, cancellationToken);
            toolFunctions.AddRange(openApiResult.Functions);
            if (openApiResult.Functions.Count > 0)
                _logger?.CreateLogger<AgentBuilder>().LogInformation(
                    "Successfully integrated {Count} OpenAPI functions from {Sources} source(s)",
                    openApiResult.Functions.Count, _openApiSources.Count);
        }

        var mergedOptions = MergeToolFunctions(_config.Provider?.DefaultChatOptions, toolFunctions);

        // Create custom summarizer client if configured
        IChatClient? summarizerClient = null;
        if (_config.HistoryReduction?.SummarizerProvider != null)
        {
            var summarizerProviderKey = _config.HistoryReduction.SummarizerProvider.ProviderKey;
            var summarizerProviderFeatures = _providerRegistry.GetProvider(summarizerProviderKey);

            if (summarizerProviderFeatures == null)
            {
                var availableProviders = string.Join(", ", _providerRegistry.GetRegisteredProviders());
                throw new InvalidOperationException(
                    $"Unknown provider for summarization: '{summarizerProviderKey}'. " +
                    $"Available providers: [{availableProviders}]");
            }

            summarizerClient = summarizerProviderFeatures.CreateChatClient(
                _config.HistoryReduction.SummarizerProvider,
                _serviceProvider);
        }

        // Create the provider-specific error handler
        var errorHandler = providerFeatures.CreateErrorHandler();
        if (errorHandler == null)
            throw new InvalidOperationException($"The factory for provider '{providerKey}' returned a null error handler.");

        // Return dependencies instead of creating agent
        return new AgentBuildDependencies(
            clientToUse,
            mergedOptions,
            errorHandler,
            summarizerClient,
            openApiResult?.OwnedHttpClients.Count > 0 ? openApiResult.OwnedHttpClients : null);
    }

    public bool IsProviderRegistered(string providerKey) => _providerRegistry.IsRegistered(providerKey);
    
    public IReadOnlyCollection<string> GetAvailableProviders() => _providerRegistry.GetRegisteredProviders();

    private static string Capitalize(string s) => string.IsNullOrEmpty(s) ? s : char.ToUpperInvariant(s[0]) + s[1..];




    /// <summary>
    /// Merges Toolkit functions into chat options.
    /// </summary>
    private ChatOptions? MergeToolFunctions(ChatOptions? defaultOptions, List<AIFunction> toolFunctions)
    {
        if (toolFunctions.Count == 0)
            return defaultOptions;

        var options = defaultOptions ?? new ChatOptions();

        // Add Toolkit functions to existing tools
        var allTools = new List<AITool>(options.Tools ?? []);
        allTools.AddRange(toolFunctions);

        // Translate ToolSelectionConfig to ChatToolMode (FFI-friendly → M.E.AI)
        var toolMode = TranslateToolMode(_config.ToolSelection);

        return new ChatOptions
        {
            Tools = allTools,
            ToolMode = toolMode,
            MaxOutputTokens = options.MaxOutputTokens,
            Temperature = options.Temperature,
            TopP = options.TopP,
            FrequencyPenalty = options.FrequencyPenalty,
            PresencePenalty = options.PresencePenalty,
            ResponseFormat = options.ResponseFormat,
            Seed = options.Seed,
            StopSequences = options.StopSequences,
            ModelId = options.ModelId,
            AdditionalProperties = options.AdditionalProperties
        };
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
    /// Used by extension methods and Toolkits to access configuration values.
    /// </summary>
    public IConfiguration? Configuration => _configuration;

    /// <summary>
    /// Gets the configuration object for this builder.
    /// Used by provider extension methods to configure provider-specific settings.
    /// </summary>
    public AgentConfig Config => _config;

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
    internal IServiceProvider? ServiceProvider => _serviceProvider;

    /// <summary>
    /// Gets the logger factory for use in extension methods.
    /// Used by MCP and other extension methods to create loggers.
    /// </summary>
    public ILoggerFactory? Logger => _logger;

    /// <summary>
    /// Internal access to Collapsed Middleware manager for extension methods
    /// </summary>

    /// <summary>
    /// Internal access to default Toolkit context for extension methods
    /// </summary>
    internal IToolMetadata? DefaulTMetadata
    {
        get => _defaulTMetadata;
        set => _defaulTMetadata = value;
    }

    /// <summary>
    /// Public access to Toolkit contexts for extension methods and external configuration
    /// </summary>
    public Dictionary<string, IToolMetadata?> ToolkitContexts => _toolkitContexts;

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
    /// This method is intended primarily for FFI integration with native Toolkits.
    /// </summary>
    public AgentBuilder WithNativeFunction(AIFunction function)
    {
        // Get or create default chat options
        if (_config.Provider == null)
            _config.Provider = new ProviderConfig();
            
        if (_config.Provider.DefaultChatOptions == null)
            _config.Provider.DefaultChatOptions = new ChatOptions();
            
        // Add to tools list
        var tools = _config.Provider.DefaultChatOptions.Tools?.ToList() ?? new List<AITool>();
        tools.Add(function);
        _config.Provider.DefaultChatOptions.Tools = tools;

        // Enable auto tool mode if not already set
        if (_config.Provider.DefaultChatOptions.ToolMode == null)
            _config.Provider.DefaultChatOptions.ToolMode = ChatToolMode.Auto;
            
        return this;
    }

    /// <summary>
    /// Creates a chat reducer based on the HistoryReductionConfig strategy.
    /// Returns null if history reduction is not enabled.
    /// </summary>
    private static IChatReducer? CreateChatReducer(
        IChatClient baseClient,
        AgentConfig config,
        IChatClient? summarizerClient)
    {
        var historyConfig = config.HistoryReduction;

        if (historyConfig == null || !historyConfig.Enabled)
        {
            return null;
        }

        return historyConfig.Strategy switch
        {
            HistoryReductionStrategy.MessageCounting =>
                new MessageCountingChatReducer(historyConfig.TargetCount),

            HistoryReductionStrategy.Summarizing =>
                CreateSummarizingReducer(baseClient, historyConfig, summarizerClient),

            _ => throw new ArgumentException($"Unknown history reduction strategy: {historyConfig.Strategy}")
        };
    }

    /// <summary>
    /// Creates a SummarizingChatReducer with custom configuration.
    /// Supports using a separate, cheaper model for summarization (cost optimization).
    /// </summary>
    private static SummarizingChatReducer CreateSummarizingReducer(
        IChatClient baseClient,
        HistoryReductionConfig historyConfig,
        IChatClient? summarizerClient)
    {
        // Determine which chat client to use for summarization
        // If a custom summarizer client was provided, use it
        // Otherwise, fall back to the base client
        var clientForSummarization = summarizerClient ?? baseClient;

        var reducer = new SummarizingChatReducer(
            clientForSummarization,
            historyConfig.TargetCount,
            historyConfig.SummarizationThreshold);

        if (!string.IsNullOrEmpty(historyConfig.CustomSummarizationPrompt))
        {
            reducer.SummarizationPrompt = historyConfig.CustomSummarizationPrompt;
        }

        return reducer;
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
    /// Supports Collapsing via extension methods (.AsGlobal(), .ForToolkit(), .ForSkill(), .ForFunction()).
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
    /// Enables tool Collapsing middleware for Toolkit collapsing and skills architecture.
    /// When enabled, Toolkits and skills are hidden behind container functions,
    /// reducing the initial tool list and cognitive load on the LLM.
    /// </summary>
    /// <param name="builder">The agent builder</param>
    /// <returns>The builder for chaining</returns>
    /// <remarks>
    /// <para>
    /// Tool Collapsing allows you to organize functions hierarchically:
    /// - Toolkit containers: Hide member functions until Toolkit is expanded
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
    ///     .WithTools&lt;FinancialToolkit&gt;()
    ///     .WithToolCollapsing()  // Enable tool Collapsing
    ///     .Build();
    /// </code>
    /// </example>
    public static AgentBuilder WithToolCollapsing(this AgentBuilder builder)
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
    ///     .WithTools&lt;FinancialToolkit&gt;()
    ///     .WithToolCollapsing(config =>
    ///     {
    ///         config.CollapseClientTools = true;
    ///         config.MaxFunctionNamesInDescription = 5;
    ///     })
    ///     .Build();
    /// </code>
    /// </example>
    public static AgentBuilder WithToolCollapsing(this AgentBuilder builder, Action<CollapsingConfig> configure)
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
    ///     .WithTools&lt;FinancialToolkit&gt;()
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
    /// <summary>
    /// Configures conversation history reduction to manage context window size.
    /// Supports message-based reduction (keeps last N messages) or summarization (uses LLM to compress old messages).
    /// </summary>
    /// <param name="builder">The agent builder instance</param>
    /// <param name="configure">Configuration action for history reduction settings</param>
    /// <returns>The builder for method chaining</returns>
    /// <example>
    /// <code>
    /// builder.WithHistoryReduction(config => {
    ///     config.Enabled = true;
    ///     config.Strategy = HistoryReductionStrategy.Summarizing;
    ///     config.TargetCount = 30;
    ///     config.SummarizationThreshold = 10;
    /// });
    /// </code>
    /// </example>
    public static AgentBuilder WithHistoryReduction(this AgentBuilder builder, Action<HistoryReductionConfig>? configure = null)
    {
        var config = builder.Config.HistoryReduction ?? new HistoryReductionConfig();
        configure?.Invoke(config);

        builder.Config.HistoryReduction = config;
        return builder;
    }

    /// <summary>
    /// Enables simple message counting reduction (keeps last N messages).
    /// Quick setup method for basic history management.
    /// </summary>
    /// <param name="builder">The agent builder instance</param>
    /// <param name="targetMessageCount">Number of messages to keep (default: 20)</param>
    /// <param name="threshold">Extra messages allowed before reduction triggers (default: 5)</param>
    /// <returns>The builder for method chaining</returns>
    public static AgentBuilder WithMessageCountingReduction(this AgentBuilder builder, int targetMessageCount = 20, int threshold = 5)
    {
        return builder.WithHistoryReduction(config =>
        {
            config.Enabled = true;
            config.Strategy = HistoryReductionStrategy.MessageCounting;
            config.TargetCount = targetMessageCount;
            config.SummarizationThreshold = threshold;
        });
    }

    /// <summary>
    /// Enables summarizing reduction (uses LLM to compress old messages).
    /// Provides better context retention than message counting but requires additional LLM calls.
    /// </summary>
    /// <param name="builder">The agent builder instance</param>
    /// <param name="targetMessageCount">Number of messages to keep (default: 20)</param>
    /// <param name="threshold">Extra messages before summarization triggers (default: 5)</param>
    /// <param name="customPrompt">Optional custom summarization prompt</param>
    /// <returns>The builder for method chaining</returns>
    public static AgentBuilder WithSummarizingReduction(this AgentBuilder builder, int targetMessageCount = 20, int threshold = 5, string? customPrompt = null)
    {
        return builder.WithHistoryReduction(config =>
        {
            config.Enabled = true;
            config.Strategy = HistoryReductionStrategy.Summarizing;
            config.TargetCount = targetMessageCount;
            config.SummarizationThreshold = threshold;
            config.CustomSummarizationPrompt = customPrompt;
        });
    }

    /// <summary>
    /// Configures a separate LLM provider for summarization to optimize costs.
    /// Use a cheaper/faster model for summaries while keeping your main model for responses.
    /// </summary>
    /// <param name="builder">The agent builder instance</param>
    /// <param name="providerKey">The provider key to use for summarization (e.g., "openai", "anthropic")</param>
    /// <param name="modelName">The model name (e.g., "gpt-4o-mini", "claude-3-haiku-20240307")</param>
    /// <param name="apiKey">Optional API key (uses main provider's key if not specified)</param>
    /// <returns>The builder for method chaining</returns>
    /// <example>
    /// <code>
    /// builder
    ///     .WithOpenAI(apiKey, "gpt-4") // Main agent uses GPT-4
    ///     .WithSummarizingReduction()
    ///     .WithSummarizerProvider("openai", "gpt-4o-mini"); // Summaries use mini
    /// </code>
    /// </example>
    public static AgentBuilder WithSummarizerProvider(this AgentBuilder builder, string providerKey, string modelName, string? apiKey = null)
    {
        var config = builder.Config.HistoryReduction ?? new HistoryReductionConfig { Enabled = true };

        config.SummarizerProvider = new ProviderConfig
        {
            ProviderKey = providerKey,
            ModelName = modelName,
            ApiKey = apiKey ?? builder.Config.Provider?.ApiKey
        };

        builder.Config.HistoryReduction = config;
        return builder;
    }

    /// <summary>
    /// Configures a separate LLM provider for summarization with full provider configuration.
    /// Use this for advanced scenarios requiring custom endpoints or options.
    /// </summary>
    /// <param name="builder">The agent builder instance</param>
    /// <param name="configureSummarizer">Action to configure the summarizer provider</param>
    /// <returns>The builder for method chaining</returns>
    /// <example>
    /// <code>
    /// builder
    ///     .WithSummarizingReduction()
    ///     .WithSummarizerProvider(config => {
    ///         config.ProviderKey = "ollama";
    ///         config.ModelName = "llama3.2";
    ///         config.Endpoint = "http://localhost:11434";
    ///     });
    /// </code>
    /// </example>
    public static AgentBuilder WithSummarizerProvider(this AgentBuilder builder, Action<ProviderConfig> configureSummarizer)
    {
        var historyConfig = builder.Config.HistoryReduction ?? new HistoryReductionConfig { Enabled = true };
        var summarizerConfig = new ProviderConfig();

        configureSummarizer(summarizerConfig);
        historyConfig.SummarizerProvider = summarizerConfig;

        builder.Config.HistoryReduction = historyConfig;
        return builder;
    }
}
#endregion

#region Toolkit Extensions


/// <summary>
/// Extension methods for configuring Toolkits for the AgentBuilder.
/// </summary>
public static class AgentBuilderToolkitExtensions
{
    /// <summary>
    /// Registers a toolkit by type with optional execution context.
    /// AOT-Compatible: Uses generated ToolkitRegistry.All catalog (zero reflection in hot path).
    /// Automatically loads toolkit registry from the assembly where T is defined if not already loaded.
    /// Auto-registers referenced toolkits from skills via GetReferencedToolkits().
    /// WARNING: For Native AOT, requires ToolkitRegistry types in all referenced assemblies to be preserved.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown if toolkit is not found in any loaded registry.</exception>
    [RequiresUnreferencedCode("Toolkit loading via WithToolkit requires ToolkitRegistry from assembly where T is defined to be preserved.")]
    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "RequiresUnreferencedCode declared on method")]
    public static AgentBuilder WithToolkit<T>(this AgentBuilder builder, IToolMetadata? context = null) where T : class, new()
    {
        var toolkitName = typeof(T).Name;
        var toolkitAssembly = typeof(T).Assembly;

        // Try to find in already loaded toolkits
        if (!builder._availableToolkits.TryGetValue(toolkitName, out var factory))
        {
            // Not found - try loading the assembly where T is defined
            builder.LoadToolRegistryFromAssembly(toolkitAssembly);

            // Try again after loading
            if (!builder._availableToolkits.TryGetValue(toolkitName, out factory))
            {
                throw new InvalidOperationException(
                    $"Toolkit '{toolkitName}' not found in ToolkitRegistry.All. " +
                    $"Ensure the toolkit class has [AIFunction], [Skill], or [SubAgent] attributes and the source generator ran successfully.");
            }
        }

        // AOT-compatible path: Use catalog
        builder._selectedToolkitFactories.Add(factory);

        // Track as explicitly registered (for ToolVisibilityManager)
        builder._explicitlyRegisteredToolkits.Add(toolkitName);

        // Store context
        builder.ToolkitContexts[toolkitName] = context;

        // Auto-discover skill dependencies using catalog (zero reflection)
        AutoRegisterDependenciesFromFactory(builder, factory);

        return builder;
    }

    /// <summary>
    /// Registers a toolkit and configures per-toolkit options such as DI-provided scoped middleware
    /// . Use this overload when your toolkit-scoped middleware requires constructor
    /// parameters that cannot be expressed via a parameterless constructor in
    /// <c>[Collapse(Middlewares = [typeof(T)])]</c>.
    /// </summary>
    /// <example>
    /// <code>
    /// builder.WithToolkit&lt;DatabaseToolkit&gt;(opts =>
    ///     opts.AddScopedMiddleware(new DbAuditMiddleware(sp.GetRequiredService&lt;IAuditLog&gt;())));
    /// </code>
    /// </example>
    [RequiresUnreferencedCode("Toolkit loading via WithToolkit requires ToolkitRegistry from assembly where T is defined to be preserved.")]
    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "RequiresUnreferencedCode declared on method")]
    public static AgentBuilder WithToolkit<T>(this AgentBuilder builder, Action<ToolkitOptions> configure, IToolMetadata? context = null) where T : class, new()
    {
        // Register the toolkit normally first
        builder.WithToolkit<T>(context);

        // Apply per-toolkit options
        var options = new ToolkitOptions();
        configure(options);

        if (options.ScopedMiddlewares.Count > 0)
        {
            var toolkitName = typeof(T).Name;
            if (!builder._toolkitScopedMiddlewares.TryGetValue(toolkitName, out var list))
            {
                list = new List<Middleware.IAgentMiddleware>();
                builder._toolkitScopedMiddlewares[toolkitName] = list;
            }
            list.AddRange(options.ScopedMiddlewares);
        }

        return builder;
    }

    /// <summary>
    /// Registers a toolkit using a pre-created instance with optional execution context.
    /// Used for DI-required toolkits (e.g., AgentPlanToolkit, DynamicMemoryToolkit).
    /// The instance's generated Registration class is used for function creation (AOT-compatible).
    /// WARNING: For Native AOT, requires ToolkitRegistry from instance assembly to be preserved.
    /// </summary>
    [RequiresUnreferencedCode("Toolkit instance registration requires ToolkitRegistry from instance's assembly to be preserved.")]
    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "RequiresUnreferencedCode declared on method")]
    public static AgentBuilder WithToolkit<T>(this AgentBuilder builder, T instance, IToolMetadata? context = null) where T : class
    {
        var toolkitName = typeof(T).Name;

        // Register as instance registration (will use generated Registration class for function creation)
        builder._instanceRegistrations.Add(new ToolInstanceRegistration(instance, toolkitName));
        builder.ToolkitContexts[toolkitName] = context;

        // Track this as explicitly registered
        builder._explicitlyRegisteredToolkits.Add(toolkitName);

        // Auto-register dependencies if toolkit is in catalog (for skill dependencies)
        // First try to load the assembly where the instance type is defined
        builder.LoadToolRegistryFromAssembly(instance.GetType().Assembly);
        if (builder._availableToolkits.TryGetValue(toolkitName, out var factory))
        {
            AutoRegisterDependenciesFromFactory(builder, factory);
        }

        return builder;
    }

    /// <summary>
    /// Registers a toolkit by Type with optional execution context.
    /// AOT-Compatible: Uses generated ToolkitRegistry.All catalog (zero reflection in hot path).
    /// Automatically loads toolkit registry from the assembly where toolkitType is defined if not already loaded.
    /// Auto-registers referenced toolkits from skills via GetReferencedToolkits().
    /// WARNING: For Native AOT, requires ToolkitRegistry from toolkitType's assembly to be preserved.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown if toolkit is not found in any loaded registry.</exception>
    [RequiresUnreferencedCode("Toolkit registration by Type requires ToolkitRegistry from toolkit assembly to be preserved.")]
    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "RequiresUnreferencedCode declared on method")]
    public static AgentBuilder WithToolkit(this AgentBuilder builder, Type toolkitType, IToolMetadata? context = null)
    {
        var toolkitName = toolkitType.Name;
        var toolkitAssembly = toolkitType.Assembly;

        // Try to find in already loaded toolkits
        if (!builder._availableToolkits.TryGetValue(toolkitName, out var factory))
        {
            // Not found - try loading the assembly where toolkitType is defined
            builder.LoadToolRegistryFromAssembly(toolkitAssembly);

            // Try again after loading
            if (!builder._availableToolkits.TryGetValue(toolkitName, out factory))
            {
                throw new InvalidOperationException(
                    $"Toolkit '{toolkitName}' not found in ToolkitRegistry.All. " +
                    $"Ensure the toolkit class has [AIFunction], [Skill], or [SubAgent] attributes and the source generator ran successfully.");
            }
        }

        // AOT-compatible path: Use catalog
        builder._selectedToolkitFactories.Add(factory);

        // Track as explicitly registered (for ToolVisibilityManager)
        builder._explicitlyRegisteredToolkits.Add(toolkitName);

        // Store context
        builder.ToolkitContexts[toolkitName] = context;

        // Auto-discover skill dependencies using catalog (zero reflection)
        AutoRegisterDependenciesFromFactory(builder, factory);

        return builder;
    }

    // ============================================
    // MIDDLEWARE STATE ASSEMBLY REGISTRATION
    // ============================================

    /// <summary>
    /// Explicitly loads middleware state factories from the assembly containing the specified marker type.
    /// Use this for assemblies that have [MiddlewareState] types but no toolkits.
    /// For assemblies with toolkits, state registries are loaded automatically via WithToolkit&lt;T&gt;().
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
    /// Use this for assemblies that have [MiddlewareState] types but no toolkits.
    /// For assemblies with toolkits, state registries are loaded automatically via WithToolkit&lt;T&gt;().
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

    // ============================================
    // DEPRECATED: WithTools methods (use WithToolkit instead)
    // ============================================

    /// <inheritdoc cref="WithToolkit{T}(AgentBuilder, IToolMetadata?)"/>
    [Obsolete("Use WithToolkit<T>() instead. WithTools will be removed in a future version.")]
    [RequiresUnreferencedCode("Toolkit loading via WithTools requires ToolkitRegistry from assembly where T is defined to be preserved.")]
    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "RequiresUnreferencedCode declared on method")]
    public static AgentBuilder WithTools<T>(this AgentBuilder builder, IToolMetadata? context = null) where T : class, new()
        => builder.WithToolkit<T>(context);

    /// <inheritdoc cref="WithToolkit{T}(AgentBuilder, T, IToolMetadata?)"/>
    [Obsolete("Use WithToolkit<T>() instead. WithTools will be removed in a future version.")]
    [RequiresUnreferencedCode("Toolkit instance registration requires ToolkitRegistry from instance's assembly to be preserved.")]
    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "RequiresUnreferencedCode declared on method")]
    public static AgentBuilder WithTools<T>(this AgentBuilder builder, T instance, IToolMetadata? context = null) where T : class
        => builder.WithToolkit(instance, context);

    /// <inheritdoc cref="WithToolkit(AgentBuilder, Type, IToolMetadata?)"/>
    [Obsolete("Use WithToolkit() instead. WithTools will be removed in a future version.")]
    [RequiresUnreferencedCode("Toolkit registration by Type requires ToolkitRegistry from toolkit assembly to be preserved.")]
    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "RequiresUnreferencedCode declared on method")]
    public static AgentBuilder WithTools(this AgentBuilder builder, Type toolkitType, IToolMetadata? context = null)
        => builder.WithToolkit(toolkitType, context);

    /// <summary>
    /// Auto-registers Toolkits referenced by skills using the Toolkit catalog (zero reflection).
    /// Phase 4.5: Also stores function filters for selective registration.
    /// </summary>
    private static void AutoRegisterDependenciesFromFactory(AgentBuilder builder, ToolkitFactory factory)
    {
        var dependencies = factory.GetReferencedToolkits();

        // Phase 4.5: Get function-specific references for selective registration
        var referencedFunctions = factory.GetReferencedFunctions();

        foreach (var depName in dependencies)
        {
            // Check if already selected
            if (builder._selectedToolkitFactories.Any(f => f.Name.Equals(depName, StringComparison.OrdinalIgnoreCase)))
                continue;

            // Look up in catalog
            if (builder._availableToolkits!.TryGetValue(depName, out var depFactory))
            {
                builder._selectedToolkitFactories.Add(depFactory);
                // Note: Dependencies are NOT added to _explicitlyRegisteredToolkits
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
        builder.Config.Provider = new ProviderConfig
        {
            ProviderKey = providerKey,
            ModelName = modelName,
            ApiKey = apiKey
        };
        return builder;
    }
}

#endregion


#region Provider

/// <summary>
/// Represents connection information parsed from a connection string.
/// Supports format: "Provider=openrouter;AccessKey=sk-xxx;Model=gpt-4;Endpoint=https://..."
/// </summary>
public class ProviderConnectionInfo
{
    /// <summary>
    /// The provider key (e.g., "openrouter", "openai", "azure", "ollama")
    /// </summary>
    public required string Provider { get; init; }

    /// <summary>
    /// The API key or access token
    /// </summary>
    public string? AccessKey { get; init; }

    /// <summary>
    /// The model name (e.g., "gpt-4", "google/gemini-2.5-pro")
    /// </summary>
    public string? Model { get; init; }

    /// <summary>
    /// The API endpoint (optional, uses provider default if not specified)
    /// </summary>
    public Uri? Endpoint { get; init; }

    /// <summary>
    /// Tries to parse a connection string into ProviderConnectionInfo
    /// </summary>
    /// <param name="connectionString">Connection string to parse</param>
    /// <param name="info">Parsed connection info if successful</param>
    /// <returns>True if parsing succeeded, false otherwise</returns>
    public static bool TryParse(string? connectionString, [NotNullWhen(true)] out ProviderConnectionInfo? info)
    {
        info = null;

        if (string.IsNullOrWhiteSpace(connectionString))
            return false;

        try
        {
            // Use DbConnectionStringBuilder for robust parsing
            var builder = new DbConnectionStringBuilder
            {
                ConnectionString = connectionString
            };

            // Provider is required
            if (!builder.ContainsKey("Provider"))
                return false;

            var provider = builder["Provider"].ToString();
            if (string.IsNullOrWhiteSpace(provider))
                return false;

            // Parse optional fields
            string? accessKey = builder.ContainsKey("AccessKey")
                ? builder["AccessKey"].ToString()
                : null;

            string? model = builder.ContainsKey("Model")
                ? builder["Model"].ToString()
                : null;

            Uri? endpoint = null;
            if (builder.ContainsKey("Endpoint"))
            {
                var endpointStr = builder["Endpoint"].ToString();
                if (!string.IsNullOrWhiteSpace(endpointStr))
                {
                    if (!Uri.TryCreate(endpointStr, UriKind.Absolute, out endpoint))
                        return false; // Invalid endpoint URL
                }
            }

            info = new ProviderConnectionInfo
            {
                Provider = provider,
                AccessKey = accessKey,
                Model = model,
                Endpoint = endpoint
            };

            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Parses a connection string, throwing an exception if invalid
    /// </summary>
    /// <param name="connectionString">Connection string to parse</param>
    /// <returns>Parsed connection info</returns>
    /// <exception cref="ArgumentException">Thrown if connection string is invalid</exception>
    public static ProviderConnectionInfo Parse(string connectionString)
    {
        if (TryParse(connectionString, out var info))
            return info;

        throw new ArgumentException(
            $"Invalid connection string: '{connectionString}'. " +
            "Expected format: 'Provider=openrouter;AccessKey=sk-xxx;Model=gpt-4;Endpoint=https://...' " +
            "(AccessKey, Model, and Endpoint are optional)",
            nameof(connectionString));
    }
}

#endregion