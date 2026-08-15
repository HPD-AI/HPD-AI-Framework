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

    /// <summary>
    /// Instance-based registrations for DI-required ToolHarnesses (e.g., AgentPlanToolHarness, DynamicMemoryToolHarness).
    /// These ToolHarnesses cannot be instantiated via the catalog because they require constructor parameters.
    /// </summary>
    public readonly List<ToolInstanceRegistration> _instanceRegistrations = new();
    // store individual ToolHarness contexts
    internal readonly Dictionary<string, IToolMetadata?> _toolharnessContexts = new();
    //  Unified content store for all agent content (skills, knowledge, memory, uploads, artifacts)
    internal IContentStore? _contentStore;
    internal ISkillStore? _skillStore;
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
    private readonly ExplicitSecretResolver _explicitSecretResolver = new();
    private bool _secretResolverChainBuilt;

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

    /// <summary>Runtime skill sources grouped by their owning registered tool harness.</summary>
    internal readonly Dictionary<string, List<ISkillSource>> _skillSources
        = new(StringComparer.OrdinalIgnoreCase);
    internal readonly Dictionary<string, List<StoredSkillSourceRegistration>> _storedSkillSources
        = new(StringComparer.OrdinalIgnoreCase);
    private SkillCatalog? _skillCatalog;

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
    /// Providers may be added explicitly by provider builder extensions.
    /// </summary>
    public AgentBuilder()
    {
        _config = new AgentConfig();
        _providerRegistry = new ProviderRegistry();

        LoadGeneratedRegistries();
    }

    /// <summary>
    /// Creates a builder from existing configuration.
    /// Providers may be added explicitly by provider builder extensions.
    /// </summary>
    public AgentBuilder(AgentConfig config)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _providerRegistry = new ProviderRegistry();

        LoadGeneratedRegistries();
    }

    /// <summary>Creates a builder backed by an immutable generated provider composition.</summary>
    /// <param name="config">The agent defaults.</param>
    /// <param name="providerComposition">The closed provider composition generated for the host.</param>
    public AgentBuilder(AgentConfig config, ProviderComposition providerComposition)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        ArgumentNullException.ThrowIfNull(providerComposition);
        _providerRegistry = new ProviderRegistry(providerComposition);
        LoadGeneratedRegistries();
        RegisterGeneratedProviders(providerComposition.Runtime);
    }

    /// <summary>Creates a default-configured builder backed by a generated provider composition.</summary>
    public AgentBuilder(ProviderComposition providerComposition)
        : this(new AgentConfig(), providerComposition)
    {
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

    private void RegisterGeneratedProviders(IProviderRuntimeRegistry runtime)
    {
        foreach (var registration in runtime.Registrations)
            _providerRegistry.Register(registration.Factory());
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
                    var selectedNames = functionFilter
                        .Select(ContainerFunctionProjection.Unqualify)
                        .ToHashSet(StringComparer.OrdinalIgnoreCase);
                    functions = ContainerFunctionProjection.Project(
                            functions,
                            function => selectedNames.Contains(ContainerFunctionProjection.Unqualify(function.Name)))
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
                // The builder owns factory registration, but the current AgentConfig still owns
                // its per-agent function projection and serialized construction data.
                if (effectiveRef.Functions is { Count: > 0 })
                    _toolFunctionFilters[effectiveRef.Name] = effectiveRef.Functions.ToArray();
                if (effectiveRef.Config.HasValue)
                    _toolharnessConfigs[effectiveRef.Name] = effectiveRef.Config.Value;
                if (_availableToolHarnesses.TryGetValue(effectiveRef.Name, out var builderFactory))
                {
                    if (effectiveRef.Metadata.HasValue && builderFactory.DeserializeMetadata != null)
                    {
                        try
                        {
                            var metadata = builderFactory.DeserializeMetadata(effectiveRef.Metadata.Value);
                            if (metadata != null)
                                _toolharnessContexts[effectiveRef.Name] = metadata;
                        }
                        catch (JsonException ex)
                        {
                            logger?.LogWarning(ex,
                                "Failed to deserialize metadata for toolharness '{Name}' to type {MetadataType}",
                                effectiveRef.Name, builderFactory.MetadataType?.Name ?? "unknown");
                        }
                    }

                    if (effectiveRef.MiddlewareConfigs is { Count: > 0 } &&
                        builderFactory.CollapseMiddlewareConfigFactories != null)
                    {
                        _toolharnessMiddlewareConfigs[effectiveRef.Name] = effectiveRef.MiddlewareConfigs;
                    }
                }
                logger?.LogDebug("ToolHarness '{Name}' already registered via builder; applied config projection", effectiveRef.Name);
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

    /// <summary>Configures the host-owned store used by deferred <c>AddSkillsFromStore</c> registrations.</summary>
    public AgentBuilder WithSkillStore(ISkillStore store)
    {
        _skillStore = store ?? throw new ArgumentNullException(nameof(store));
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
        ArgumentNullException.ThrowIfNull(serviceProvider);
        _serviceProvider = serviceProvider;
        _logger = serviceProvider.GetService<ILoggerFactory>();
        if (serviceProvider.GetService<ProviderComposition>() is { } composition)
        {
            foreach (var registration in composition.Runtime.Registrations)
                _providerRegistry.Register(registration.Factory());
        }
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
    /// Adds a runtime-only secret with the highest resolution priority.
    /// Explicit secret values are never written to serializable agent configuration.
    /// </summary>
    /// <param name="key">The canonical secret-resolver key.</param>
    /// <param name="value">The secret value.</param>
    /// <returns>This builder.</returns>
    public AgentBuilder AddExplicitSecret(string key, string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        if (_secretResolverChainBuilt)
            throw new InvalidOperationException("Explicit secrets cannot be added after the agent has been built.");

        _explicitSecretResolver.Set(key, value);
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
        chatConfig.Reasoning = new ReasoningOptions
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
    /// Sets the agent name
    /// </summary>
    public AgentBuilder WithName(string name)
    {
        _config.Name = name ?? throw new ArgumentNullException(nameof(name));
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
        if (_config.Skills.ActivationLifetime != SkillActivationLifetime.MessageTurn)
            throw new InvalidOperationException(
                $"Skill activation lifetime '{_config.Skills.ActivationLifetime}' is not supported. " +
                "Use MessageTurn until its persistence semantics are implemented.");
        await ResolveStoredAgentDefinitionAsync(cancellationToken).ConfigureAwait(false);
        EnsureAutoConfiguration();

        // Build the secret resolver chain FIRST (before BuildDependenciesAsync)
        // Providers need ISecretResolver available in the service provider during CreateChatClient
        if (!_secretResolverChainBuilt)
        {
            var resolvers = new List<ISecretResolver> { _explicitSecretResolver };
            if (_secretResolver is not null)
            {
                resolvers.Add(_secretResolver);
            }
            else
            {
                resolvers.Add(new EnvironmentSecretResolver(
                    _serviceProvider?.GetService<IProviderSecretAliasRegistry>()
                    ?? (_providerRegistry as ProviderRegistry)?.Composition?.SecretAliases));
                resolvers.AddRange(_additionalResolvers);
                if (_configuration != null)
                    resolvers.Add(new ConfigurationSecretResolver(_configuration));
            }
            _secretResolver = new ChainedSecretResolver(resolvers);
            _secretResolverChainBuilt = true;
        }

        // Wrap the service provider to make ISecretResolver available to providers
        // This allows providers to resolve secrets during CreateChatClient without
        // replacing the user's service provider
        _serviceProvider = new CompositeServiceProvider(_serviceProvider, _secretResolver);

        // Skill sources participate in epoch-zero tool construction, so their infrastructure
        // must be final before BuildDependenciesAsync invokes BuildToolOptionsAsync.
        if (_contentStore is null)
        {
            _contentStore = _serviceProvider.GetService<IContentStore>();
            if (_contentStore is null)
            {
                _contentStore = new InMemoryContentStore();
                _logger?.CreateLogger<AgentBuilder>().LogInformation(
                    "Using default InMemoryContentStore (in-memory, ephemeral). " +
                    "Use .WithContentStore() for persistence (e.g., LocalFileContentStore).");
            }
        }
        if (_storedSkillSources.Count > 0)
        {
            _skillStore ??= _serviceProvider.GetService<ISkillStore>();
            MaterializeStoredSkillSources();
        }

        var buildData = await BuildDependenciesAsync(cancellationToken).ConfigureAwait(false);
        await MaterializeSubAgentDefinitionsAsync(buildData, cancellationToken).ConfigureAwait(false);

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
        // Content storage was resolved before dependency/tool construction.

        // Resolve config middlewares before auto-middleware registration
        // This enables Config = Base, Builder = Override/Extend pattern
        ResolveConfigMiddlewares();

        ActivateRegisteredFeatures();
        RegisterAutoMiddleware(buildData);
        return CreateAgent(buildData);
    }

    private async Task MaterializeSubAgentDefinitionsAsync(
        AgentBuildDependencies buildData,
        CancellationToken cancellationToken)
    {
        var declarations = buildData.MergedOptions?.Tools?
            .OfType<AIFunction>()
            .Select(function => new
            {
                Definition = function.AdditionalProperties is not null &&
                    function.AdditionalProperties.TryGetValue("SubAgentDefinition", out var value)
                        ? value as SubAgent
                        : null,
                Owner = function.AdditionalProperties is not null &&
                    function.AdditionalProperties.TryGetValue("ParentToolHarness", out var owner)
                        ? owner?.ToString()
                        : null,
                Member = function.AdditionalProperties is not null &&
                    function.AdditionalProperties.TryGetValue("SubAgentMember", out var member)
                        ? member?.ToString()
                        : null,
                Assembly = function.AdditionalProperties is not null &&
                    function.AdditionalProperties.TryGetValue("SubAgentAssembly", out var assembly)
                        ? assembly?.ToString()
                        : null
            })
            .Where(item => item.Definition is not null)
            .ToArray() ?? [];

        if (declarations.Length == 0)
            return;

        var store = _config.AgentStore ??= AgentBuilderDefaults.AgentStore;
        var materializer = new AgentDefinitionMaterializer(store);
        foreach (var declaration in declarations)
        {
            await materializer.MaterializeAsync(
                declaration.Definition!,
                _config,
                $"{declaration.Assembly ?? "unknown-assembly"}:{declaration.Owner ?? "unknown-toolharness"}.{declaration.Member ?? declaration.Definition!.Name}",
                cancellationToken).ConfigureAwait(false);
        }
    }

    private void MaterializeStoredSkillSources()
    {
        var resolved = new List<(string Owner, IContentBackedSkillStore Store, SkillQuery Query)>();
        foreach (var (owner, registrations) in _storedSkillSources)
        {
            foreach (var registration in registrations)
            {
                var store = registration.Store ?? _skillStore as IContentBackedSkillStore
                    ?? throw new InvalidOperationException(
                        $"Toolharness '{owner}' requested stored skills, but no content-backed ISkillStore is configured. " +
                        "Call WithSkillStore(...), register IContentBackedSkillStore/ISkillStore in DI, or use AddSkillSource(...).");
                resolved.Add((owner, store, registration.Query));
            }
        }

        foreach (var attachment in resolved.Where(item => item.Query.IsUnfiltered))
        {
            var conflictingOwner = resolved.FirstOrDefault(item =>
                !string.Equals(item.Owner, attachment.Owner, StringComparison.Ordinal) &&
                ReferenceEquals(item.Store, attachment.Store) &&
                item.Query.IsUnfiltered).Owner;
            if (conflictingOwner is not null)
                throw new InvalidOperationException(
                    $"Stored skill store is attached without a selector to both '{attachment.Owner}' and " +
                    $"'{conflictingOwner}'. Use SkillQuery.ByIds(...), SkillQuery.WithTag(...), or another " +
                    "filtered query so each harness selects its intended skills.");
        }

        foreach (var (owner, store, query) in resolved)
        {
            if (!_skillSources.TryGetValue(owner, out var sources))
                _skillSources[owner] = sources = [];
            sources.Add(new ContentStoreSkillSource(store, store.ContentStore, query));
        }
        _storedSkillSources.Clear();
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

        var storedConfig = AgentConfigSnapshot.Create(_config);
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

        // Register ContentUploadMiddleware for intelligent file upload routing.
        // Routes DataContent to HostedFileClient (provider-native) or IContentStore based on
        // provider capabilities and RunConfig.UploadStrategy (Auto/Hosted/Local).
        // _contentStore is guaranteed to be non-null due to auto-initialization in Build().
        _middlewares.Add(new Middleware.ContentUploadMiddleware(_contentStore));

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
        // - RetryMiddleware wraps timeout (retry the entire timeout operation)
        // - FunctionTimeoutMiddleware wraps execution (timeout individual attempts)

        // Register RetryMiddleware if retry is enabled
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
                _config.Skills,
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

        // Compaction observes the model-visible result produced by framework input mutators.
        // It remains ordinary iteration middleware; registration order supplies the boundary.
        if (_config.Compaction is not null)
        {
            _middlewares.Add(new CompactionMiddleware
            {
                Config = _config.Compaction
            });
        }

        // Run after every framework tool-list mutator so unavailable thread-native
        // subagents cannot be reintroduced by collapsing or client-tool projection.
        if (buildData.MergedOptions?.Tools is not null)
            _middlewares.Add(new SubAgentAvailabilityMiddleware(buildData.MergedOptions.Tools));

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
        var agent = new Agent(
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
        if (_skillCatalog is not null)
            agent.SetSkillCatalog(
                _skillCatalog,
                _skillSources.SelectMany(pair => pair.Value.Select(source => (
                    Source: source,
                    Context: new SkillSourceContext(_config.Name, pair.Key, null, _serviceProvider)))));
        return agent;
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
        var staticFunctions = toolFunctions.ToArray();
        var staticMetadata = staticFunctions
            .Where(function => TryGetCapabilityMetadata(function, out _))
            .ToDictionary(function => function, function => GetCapabilityMetadata(function));
        var initialSnapshot = await BuildSkillSnapshotAsync(
            0,
            staticFunctions,
            staticMetadata,
            cancellationToken).ConfigureAwait(false);
        toolFunctions.AddRange(initialSnapshot.Functions.Where(function => !staticFunctions.Contains(function)));
        _skillCatalog = new SkillCatalog(
            initialSnapshot,
            (epoch, token) => BuildSkillSnapshotAsync(epoch, staticFunctions, staticMetadata, token));

        var typedSkillFunctions = toolFunctions.Where(function =>
            function.AdditionalProperties?.TryGetValue(
                HPDCapabilityMetadata.AdditionalPropertiesKey,
                out var value) == true && value is HPDCapabilityMetadata).ToArray();
        if (typedSkillFunctions.Length > 0)
            _ = CapabilityGraph.CreateFromFunctions(typedSkillFunctions);

        // Middleware out container functions if Collapsing is disabled.
        // Container functions are only needed when Collapsing is enabled for the two-turn expansion flow.
        if (_config.Collapsing?.Enabled != true)
        {
            toolFunctions = toolFunctions.Where(function =>
            {
                var legacyContainer = function.AdditionalProperties?.TryGetValue("IsContainer", out var value) == true &&
                    value is bool isContainer && isContainer;
                var typedContainer = function.AdditionalProperties?.TryGetValue(
                        HPDCapabilityMetadata.AdditionalPropertiesKey,
                        out var metadataValue) == true &&
                    metadataValue is HPDCapabilityMetadata metadata &&
                    metadata.Kind is HPDCapabilityKind.SkillActivation or HPDCapabilityKind.ToolHarnessActivation;
                return !legacyContainer && !typedContainer;
            }).ToList();
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

        NormalizeCapabilityMetadata(toolFunctions);
        _ = CapabilityGraph.CreateFromFunctions(toolFunctions);

        return new AgentToolBuildResult(
            MergeToolFunctions(
                (_config.ResolveClientConfig(ProviderClientFamily.Chat) as ChatClientConfig)?.ToMicrosoftChatOptions(),
                toolFunctions),
            openApiResult?.OwnedHttpClients.Count > 0 ? openApiResult.OwnedHttpClients : null);
    }

    private async ValueTask<SkillCatalogSnapshot> BuildSkillSnapshotAsync(
        long epoch,
        IReadOnlyList<AIFunction> staticFunctions,
        IReadOnlyDictionary<AIFunction, HPDCapabilityMetadata> staticMetadata,
        CancellationToken cancellationToken)
    {
        var functions = staticFunctions.ToList();
        foreach (var (function, metadata) in staticMetadata)
        {
            if (function.AdditionalProperties is not IDictionary<string, object?> properties)
                throw new InvalidOperationException($"Function '{function.Name}' metadata cannot be normalized.");
            properties[HPDCapabilityMetadata.AdditionalPropertiesKey] = metadata;
        }

        var serialization = CreateToolSerializationOptions();
        foreach (var (owner, sources) in _skillSources)
        {
            foreach (var source in sources)
            {
                var skills = await source.GetSkillsAsync(
                    new SkillSourceContext(_config.Name, owner, null, _serviceProvider),
                    cancellationToken).ConfigureAwait(false);
                MaterializeRuntimeSkillReferences(skills, functions, serialization);
                functions.AddRange(RuntimeSkillFunctionProjector.Project(
                    owner,
                    skills,
                    functions,
                    serialization));
            }
        }

        var availableRunners = _serviceProvider?.GetServices<ISkillScriptRunner>().ToArray()
            ?? Array.Empty<ISkillScriptRunner>();
        foreach (var activation in functions.Where(function =>
            function.AdditionalProperties?.TryGetValue(
                SkillRuntimeMetadata.SkillDefinitionKey,
                out var value) == true && value is Skill))
        {
            var definition = (Skill)activation.AdditionalProperties![SkillRuntimeMetadata.SkillDefinitionKey]!;
            foreach (var script in definition.Capabilities.OfType<SkillScript>())
            {
                var matches = availableRunners.Count(runner => runner.CanRun(script));
                if (matches != 1)
                    throw new InvalidOperationException(
                        $"Skill script '{definition.Id}:{script.Name}' requires exactly one compatible runner; found {matches}.");
            }
        }

        var typed = functions.Where(function => TryGetCapabilityMetadata(function, out _)).ToImmutableArray();
        // A tool profile may intentionally materialize only part of a generated harness. Build the
        // epoch from the capabilities that actually exist in this agent, rather than retaining
        // generator edges to functions excluded by that profile.
        var materializedIds = typed
            .Select(function => GetCapabilityMetadata(function).Id)
            .ToImmutableHashSet();
        foreach (var function in typed)
        {
            var metadata = GetCapabilityMetadata(function);
            var projected = metadata with
            {
                ParentContainerIds = metadata.ParentContainerIds
                    .Where(materializedIds.Contains)
                    .ToImmutableArray(),
                Reveals = metadata.Reveals
                    .Where(materializedIds.Contains)
                    .ToImmutableArray()
            };
            if (projected != metadata)
                SetCapabilityMetadata(function, projected);
        }
        var graph = CapabilityGraph.CreateFromFunctions(typed);
        var descriptors = typed
            .Where(function => GetCapabilityMetadata(function).Kind == HPDCapabilityKind.SkillActivation)
            .Select(function =>
            {
                var metadata = GetCapabilityMetadata(function);
                var skill = function.AdditionalProperties?.TryGetValue(
                    SkillRuntimeMetadata.SkillDefinitionKey,
                    out var value) == true ? value as Skill : null;
                return skill is null ? null : new SkillDescriptor
                {
                    Id = metadata.Id,
                    ModelName = function.Name,
                    Description = function.Description,
                    Instructions = skill.Instructions,
                    Reinforcement = skill.Reinforcement,
                    Children = metadata.Reveals,
                    Lifetime = skill.Lifetime
                };
            })
            .Where(descriptor => descriptor is not null)
            .ToImmutableDictionary(descriptor => descriptor!.Id, descriptor => descriptor!);
        return new SkillCatalogSnapshot
        {
            Epoch = epoch,
            Graph = graph,
            Functions = typed,
            Skills = descriptors
        };
    }

    private void MaterializeRuntimeSkillReferences(
        IReadOnlyList<Skill> skills,
        List<AIFunction> functions,
        HPDToolSerializationOptions serialization)
    {
        foreach (var reference in skills
            .SelectMany(skill => skill.Capabilities)
            .OfType<ISkillFunctionReference>())
        {
            var alreadyMaterialized = functions.Any(function =>
                TryGetCapabilityMetadata(function, out var metadata) &&
                metadata.DeclarationMemberName == reference.MemberName &&
                metadata.Id.Value.StartsWith(
                    $"generated:{reference.ToolHarnessType.Name}.",
                    StringComparison.Ordinal));
            if (alreadyMaterialized)
                continue;

            var factory = _availableToolHarnesses.Values.FirstOrDefault(candidate =>
                candidate.ToolHarnessType == reference.ToolHarnessType)
                ?? throw new InvalidOperationException(
                    $"Runtime skill references unavailable toolharness '{reference.ToolHarnessType.FullName}'. " +
                    "Ensure its generated registry is loaded before constructing the agent.");
            var generated = factory.CreateFunctions(
                factory.CreateInstance(),
                ToolHarnessContexts.GetValueOrDefault(factory.Name),
                serialization);
            var function = generated.SingleOrDefault(candidate =>
                TryGetCapabilityMetadata(candidate, out var metadata) &&
                metadata.DeclarationMemberName == reference.MemberName)
                ?? throw new InvalidOperationException(
                    $"Runtime skill references unavailable generated function " +
                    $"'{reference.ToolHarnessType.Name}.{reference.MemberName}'.");
            functions.Add(function);
        }
    }

    private static bool TryGetCapabilityMetadata(AIFunction function, out HPDCapabilityMetadata metadata)
    {
        metadata = function.AdditionalProperties?.TryGetValue(
            HPDCapabilityMetadata.AdditionalPropertiesKey,
            out var value) == true ? value as HPDCapabilityMetadata : null!;
        return metadata is not null;
    }

    private static HPDCapabilityMetadata GetCapabilityMetadata(AIFunction function)
        => TryGetCapabilityMetadata(function, out var metadata)
            ? metadata
            : throw new InvalidOperationException($"Function '{function.Name}' lacks typed capability metadata.");

    private static void NormalizeCapabilityMetadata(List<AIFunction> functions)
    {
        var identifiers = new Dictionary<AIFunction, CapabilityId>();
        foreach (var function in functions)
        {
            if (TryGetCapabilityMetadata(function, out var existing))
            {
                identifiers[function] = existing.Id;
                continue;
            }
            var owner = function.AdditionalProperties?.TryGetValue("ParentToolHarness", out var ownerValue) == true
                ? ownerValue?.ToString()
                : null;
            identifiers[function] = CapabilityId.Create(
                $"runtime:{owner ?? "agent"}:{function.Name}");
        }

        var containersByName = functions
            .Where(IsLegacyContainer)
            .SelectMany(function => ContainerAliases(function).Select(alias => (alias, function)))
            .GroupBy(entry => entry.alias, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First().function, StringComparer.OrdinalIgnoreCase);
        var functionsByName = functions
            .GroupBy(function => function.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        foreach (var function in functions)
        {
            if (TryGetCapabilityMetadata(function, out _))
                continue;
            var parents = ImmutableArray<CapabilityId>.Empty;
            var parentName = function.AdditionalProperties?.TryGetValue("ParentContainer", out var parentValue) == true
                ? parentValue?.ToString()
                : function.AdditionalProperties?.TryGetValue("ParentToolHarness", out var harnessValue) == true
                    ? harnessValue?.ToString()
                    : null;
            if (parentName is not null && containersByName.TryGetValue(parentName, out var parent))
                parents = [identifiers[parent]];

            var children = LegacyChildren(function)
                .Select(ContainerFunctionProjection.Unqualify)
                .Where(functionsByName.ContainsKey)
                .Select(name => identifiers[functionsByName[name]])
                .Distinct()
                .ToImmutableArray();
            SetCapabilityMetadata(function, new HPDCapabilityMetadata
            {
                Id = identifiers[function],
                Kind = InferCapabilityKind(function),
                ParentContainerIds = parents,
                Reveals = children
            });
        }
    }

    private static void SetCapabilityMetadata(AIFunction function, HPDCapabilityMetadata metadata)
    {
        if (function.AdditionalProperties is not IDictionary<string, object?> properties)
            throw new InvalidOperationException($"Function '{function.Name}' metadata cannot be normalized.");
        properties[HPDCapabilityMetadata.AdditionalPropertiesKey] = metadata;
    }

    private static bool IsLegacyContainer(AIFunction function)
        => function.AdditionalProperties?.TryGetValue("IsContainer", out var value) == true && value is true;

    private static IEnumerable<string> ContainerAliases(AIFunction function)
    {
        yield return function.Name;
        if (function.AdditionalProperties?.TryGetValue("ToolHarnessName", out var value) == true && value is string name)
            yield return name;
    }

    private static IEnumerable<string> LegacyChildren(AIFunction function)
    {
        foreach (var key in new[] { "ReferencedFunctions", "ChildFunctions" })
        {
            if (function.AdditionalProperties?.TryGetValue(key, out var value) == true && value is string[] children)
            {
                foreach (var child in children)
                    yield return child;
            }
        }
    }

    private static HPDCapabilityKind InferCapabilityKind(AIFunction function)
    {
        if (IsLegacyContainer(function))
            return HPDCapabilityKind.ToolHarnessActivation;
        if (function.AdditionalProperties?.TryGetValue("IsSubAgent", out var subAgent) == true && subAgent is true)
            return HPDCapabilityKind.SubAgent;
        if (function.AdditionalProperties?.TryGetValue("IsMultiAgent", out var multiAgent) == true && multiAgent is true)
            return HPDCapabilityKind.MultiAgent;
        if (function.AdditionalProperties?.TryGetValue("CapabilityType", out var type) == true && type?.ToString() == "OpenApi")
            return HPDCapabilityKind.OpenApi;
        if (function.AdditionalProperties?.ContainsKey("MCPServerName") == true)
            return HPDCapabilityKind.Mcp;
        return HPDCapabilityKind.Function;
    }

    private static AgentClientsConfig? CreateRunClientOverrides(AgentRunConfig? options) => options?.Clients;

    private AgentClientSet CreateAuxiliaryClientSet()
    {
        var resolvedConfigs = new Dictionary<ProviderClientFamily, ProviderClientConfig>();
        var clients = _config?.Clients ?? new AgentClientsConfig();
        var textToSpeech = CaptureOverride(ProviderClientFamily.TextToSpeech, clients.TextToSpeech, clients.TextToSpeech?.Override?.Client, resolvedConfigs);
        var speechToText = CaptureOverride(ProviderClientFamily.SpeechToText, clients.SpeechToText, clients.SpeechToText?.Override?.Client, resolvedConfigs);
        var realtime = CaptureOverride(ProviderClientFamily.Realtime, clients.Realtime, clients.Realtime?.Override?.Client, resolvedConfigs);
        var imageGenerator = CaptureOverride(ProviderClientFamily.ImageGeneration, clients.ImageGeneration, clients.ImageGeneration?.Override?.Client, resolvedConfigs);
        var embeddingGenerator = CaptureOverride(ProviderClientFamily.Embeddings, clients.Embeddings, clients.Embeddings?.Override?.Client, resolvedConfigs);
        var hostedFiles = CaptureOverride(ProviderClientFamily.HostedFiles, clients.HostedFiles, clients.HostedFiles?.Override?.Client, resolvedConfigs);
        var eotFactory = ResolveComponentFactory<IEndOfTurnDetectorProvider, IEotDetector>(ProviderClientFamily.EndOfTurnDetection, ProviderFamilyLifetime.StatefulPerAudioSession, static (p, c, x, s) => p.CreateEndOfTurnDetector(c, x, s), resolvedConfigs);

        return new AgentClientSet
        {
            TextToSpeech = textToSpeech,
            SpeechToText = speechToText,
            Realtime = realtime,
            ImageGenerator = imageGenerator,
            EmbeddingGenerator = embeddingGenerator,
            HostedFiles = hostedFiles,
            EndOfTurnDetectorFactory = eotFactory,
            ResolvedConfigs = resolvedConfigs
        };
    }

    private static TClient? CaptureOverride<TClient>(
        ProviderClientFamily family,
        ProviderClientConfig? config,
        TClient? client,
        Dictionary<ProviderClientFamily, ProviderClientConfig> resolvedConfigs)
        where TClient : class
    {
        if (client is null)
            return null;

        if (config is not null)
            resolvedConfigs[family] = ProviderClientConfigResolver.Clone(config);
        return client;
    }

    private Func<ProviderComponentLifetimeContext, TComponent>? ResolveComponentFactory<TProvider, TComponent>(
        ProviderClientFamily family,
        ProviderFamilyLifetime defaultLifetime,
        Func<TProvider, ProviderClientConfig, ProviderComponentLifetimeContext, IServiceProvider?, TComponent> createComponent,
        Dictionary<ProviderClientFamily, ProviderClientConfig> resolvedConfigs)
        where TProvider : class, IProvider
    {
        var config = _config.ResolveClientConfig(family);
        if (config == null || string.IsNullOrWhiteSpace(config.ProviderKey))
            return null;

        var provider = _providerRegistry.GetRequiredProvider<TProvider>(config.ProviderKey);
        var capturedConfig = ProviderClientConfigResolver.Clone(config);
        resolvedConfigs[family] = capturedConfig;
        var lifetime = provider.GetMetadata().Families.TryGetValue(family, out var descriptor) ? descriptor.Lifetime : defaultLifetime;
        return context =>
        {
            var scopedContext = context.Lifetime == ProviderFamilyLifetime.ReusableClient ? context with { Lifetime = lifetime } : context;
            return ApplyComponentMiddleware(family, createComponent(provider, capturedConfig, scopedContext, _serviceProvider), scopedContext);
        };
    }

    private TComponent ApplyComponentMiddleware<TComponent>(ProviderClientFamily family, TComponent component, ProviderComponentLifetimeContext context)
        => family switch
        {
            ProviderClientFamily.EndOfTurnDetection when component is IEotDetector value => (TComponent)(object)ApplyMiddleware(value, _config.ClientMiddleware?.EndOfTurnDetection, context, "end-of-turn detector"),
            _ => component
        };

    private TClient ApplyMiddleware<TClient>(TClient client, IReadOnlyList<Func<TClient, IServiceProvider?, TClient>>? middleware, string description)
    {
        if (middleware == null) return client;
        var effective = client;
        for (var index = middleware.Count - 1; index >= 0; index--)
            effective = middleware[index](effective, _serviceProvider) ?? throw new InvalidOperationException($"{description} middleware returned null.");
        return effective;
    }

    private TComponent ApplyMiddleware<TComponent>(TComponent component, IReadOnlyList<Func<TComponent, ProviderComponentLifetimeContext, IServiceProvider?, TComponent>>? middleware, ProviderComponentLifetimeContext context, string description)
    {
        if (middleware == null) return component;
        var effective = component;
        for (var index = middleware.Count - 1; index >= 0; index--)
            effective = middleware[index](effective, context, _serviceProvider) ?? throw new InvalidOperationException($"{description} middleware returned null.");
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
            return new AgentBuildDependencies(
                AgentClientSet.ForChat(_baseClient),
                toolBuild.MergedOptions,
                testErrorHandler,
                OwnedHttpClients: toolBuild.OwnedHttpClients);
        }

        // Provider-backed clients are invocation resources. The builder retains only
        // their defaults and runtime services; the run resolver acquires clients lazily.
        AgentConfigValidator.ValidateAndThrow(_config);
        var runtimeToolBuild = await BuildToolOptionsAsync(cancellationToken).ConfigureAwait(false);
        return new AgentBuildDependencies(
            CreateAuxiliaryClientSet(),
            runtimeToolBuild.MergedOptions,
            new HPD.Agent.ErrorHandling.GenericErrorHandler(),
            OwnedHttpClients: runtimeToolBuild.OwnedHttpClients);
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

    private ChatClientConfig EnsureChatClientConfig()
    {
        return _config.EnsureChatClientConfig();
    }

    /// <summary>
    /// Merges ToolHarness functions into chat options.
    /// </summary>
    private ChatOptions? MergeToolFunctions(ChatOptions? defaultOptions, List<AIFunction> toolFunctions)
    {
        var serverFunctions = _config.ServerConfiguredTools?.OfType<AIFunction>().ToArray() ?? [];
        if (toolFunctions.Count == 0 && serverFunctions.Length == 0)
            return defaultOptions;

        var options = defaultOptions?.Clone() ?? new ChatOptions();

        // Add ToolHarness functions to existing tools
        var allTools = new List<AITool>(options.Tools ?? []);
        foreach (var function in serverFunctions)
        {
            if (!allTools.Contains(function))
                allTools.Add(function);
        }
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
        ArgumentNullException.ThrowIfNull(function);
        _config.ServerConfiguredTools ??= new List<AITool>();
        _config.ServerConfiguredTools.Add(function);

        return this;
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
    /// Adds provider-aware retry middleware for model and function calls.
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
    /// .WithRetry()    // Outermost - retry the entire timeout operation
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
    ///     .WithRetry()  // Uses config.ErrorHandling settings
    ///     .Build();
    /// </code>
    /// </example>
    public static AgentBuilder WithRetry(this AgentBuilder builder)
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
    /// Adds provider-aware retry middleware for model and function calls with custom configuration.
    /// </summary>
    /// <param name="builder">The agent builder</param>
    /// <param name="configure">Action to configure error handling settings</param>
    /// <returns>The builder for chaining</returns>
    /// <example>
    /// <code>
    /// var agent = new AgentBuilder()
    ///     .WithRetry(config =>
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
    public static AgentBuilder WithRetry(this AgentBuilder builder, Action<ErrorHandlingConfig> configure)
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
    /// Uses <see cref="ErrorHandlingConfig.SingleFunctionTimeout"/> when configured; otherwise this
    /// explicit convenience method applies a 30-second timeout. Building an agent without calling
    /// this method does not enable function timeouts unless the configuration contains a value.
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
    /// .WithRetry()    // Outermost - retry the entire timeout operation
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
    /// .WithRetry()      // Outermost - retry the entire operation
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
    /// <item><b>RetryMiddleware</b> - Outermost, retries entire operation</item>
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
        builder.WithRetry();
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
    /// <param name="configureRetry">Optional action to configure function RetryMiddleware </param>
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
    ///         configureRetry: retry =>
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
        Action<ErrorHandlingConfig>? configureRetry = null,
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
        if (configureRetry != null)
            builder.WithRetry(configureRetry);
        else
            builder.WithRetry();

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
    /// Configures harness collapsing, which is enabled by default.
    /// </summary>
    /// <param name="builder">The agent builder</param>
    /// <param name="configure">Action that customizes collapsing behavior without changing its enabled state.</param>
    /// <returns>The builder for chaining</returns>
    /// <remarks>
    /// <para>
    /// Harness collapsing organizes functions hierarchically:
    /// - ToolHarness containers: Hide member functions until ToolHarness is expanded
    /// - Skill containers: Hide skill-specific functions until skill is activated
    /// </para>
    /// <para>
    /// This can reduce initial tool list size by up to 87.5%, improving LLM performance
    /// and reducing token usage.
    /// </para>
    /// This method preserves an explicit <see cref="CollapsingConfig.Enabled"/> value, including
    /// <see langword="false"/> from serialized configuration. Use
    /// <see cref="WithoutHarnessCollapsing(AgentBuilder)"/> to opt out explicitly.
    /// </remarks>
    /// <example>
    /// <code>
    /// var agent = new AgentBuilder()
    ///     .WithTools&lt;FinancialToolHarness&gt;()
    ///     .ConfigureHarnessCollapsing(config =>
    ///     {
    ///         config.CollapseClientTools = true;
    ///         config.MaxFunctionNamesInDescription = 5;
    ///     })
    ///     .Build();
    /// </code>
    /// </example>
    public static AgentBuilder ConfigureHarnessCollapsing(this AgentBuilder builder, Action<CollapsingConfig> configure)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configure);
        configure(builder.Config.Collapsing);
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
    ///     .WithoutHarnessCollapsing()  // All tools always visible
    ///     .Build();
    /// </code>
    /// </example>
    public static AgentBuilder WithoutHarnessCollapsing(this AgentBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
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
    /// Configures canonical thread compaction.
    /// </summary>
    /// <param name="builder">The agent builder instance</param>
    /// <param name="configure">Configuration action for compaction settings</param>
    /// <returns>The builder for method chaining</returns>
    /// <example>
    /// <code>
    /// builder.WithCompaction(config =>
    ///     config.Automatic = new AutomaticCompactionPolicy
    ///     {
    ///         Trigger = new TurnCountCompactionTrigger(10),
    ///         Compaction = new CompactionSpecification
    ///         {
    ///             Point = new CompactAtCurrentHead(),
    ///             Preservation = new PreservePreviousTurns(5),
    ///             Strategy = new SummarizingCompaction(),
    ///             CommitMode = CompactionCommitMode.Soft
    ///         }
    ///     });
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
        builder.LoadGeneratedRegistries();
        var toolharnessName = typeof(T).Name;

        var factory = GetToolHarnessFactory(builder, toolharnessName);

        // AOT-compatible path: Use catalog
        builder._selectedToolHarnessFactories.Add(factory);
        builder._builderAddedToolHarnesses.Add(toolharnessName);

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

        if (options.SkillSources.Count > 0)
        {
            var toolharnessName = typeof(T).Name;
            if (!builder._skillSources.TryGetValue(toolharnessName, out var sources))
            {
                sources = [];
                builder._skillSources[toolharnessName] = sources;
            }
            sources.AddRange(options.SkillSources);
        }

        if (options.StoredSkillSources.Count > 0)
        {
            var toolharnessName = typeof(T).Name;
            if (!builder._storedSkillSources.TryGetValue(toolharnessName, out var registrations))
                builder._storedSkillSources[toolharnessName] = registrations = [];
            registrations.AddRange(options.StoredSkillSources);
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
        builder._builderAddedToolHarnesses.Add(toolharnessName);
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
        builder._builderAddedToolHarnesses.Add(toolharnessName);

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
    /// <summary>Configures the default Chat provider and model for agent runs.</summary>
    /// <param name="builder">The agent builder.</param>
    /// <param name="providerKey">The canonical provider key.</param>
    /// <param name="modelName">The default model name.</param>
    /// <param name="apiKey">An optional runtime-only explicit API key.</param>
    /// <returns>The builder.</returns>
    public static AgentBuilder WithProvider(this AgentBuilder builder, string providerKey, string modelName, string? apiKey = null)
    {
        builder.Config.SetChatClientConfig(new ChatClientConfig
        {
            ProviderKey = providerKey,
            ModelName = modelName
        });
        if (apiKey is not null)
            builder.AddExplicitSecret($"{providerKey}:ApiKey", apiKey);
        return builder;
    }
}

#endregion


internal static class AgentBuilderDefaults
{
    internal static IAgentStore AgentStore { get; } = new InMemoryAgentStore();
}
