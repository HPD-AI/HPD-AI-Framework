using HPD.Agent.Middleware;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace HPD.Agent;

/// <summary>Identifies the component responsible for disposing an activated middleware instance.</summary>
public enum ToolHarnessMiddlewareOwnership
{
    /// <summary>The accepted input execution owns and disposes the instance.</summary>
    Execution,
    /// <summary>The accepted input's child service scope owns and disposes the instance.</summary>
    Services
}

/// <summary>Declares that a ToolHarness middleware type must resolve from the input child scope.</summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
public sealed class ToolHarnessMiddlewareLifetimeAttribute(ToolHarnessMiddlewareOwnership ownership) : Attribute
{
    /// <summary>Gets the explicitly selected ownership.</summary>
    public ToolHarnessMiddlewareOwnership Ownership { get; } = ownership;
}

/// <summary>Declares the Agent-owned implementation used for a middleware constructor resource.</summary>
[AttributeUsage(AttributeTargets.Interface, Inherited = false, AllowMultiple = false)]
public sealed class ToolHarnessAgentResourceAttribute(Type implementationType) : Attribute
{
    /// <summary>Gets the concrete async resource type created once per built Agent.</summary>
    public Type ImplementationType { get; } = implementationType ?? throw new ArgumentNullException(nameof(implementationType));
}

/// <summary>Declares the source-generated JSON context that owns Native-AOT metadata for a middleware configuration type.</summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, Inherited = false, AllowMultiple = false)]
public sealed class ToolHarnessJsonContextAttribute(Type contextType) : Attribute
{
    /// <summary>Gets the <see cref="System.Text.Json.Serialization.JsonSerializerContext"/> type containing the configuration metadata.</summary>
    public Type ContextType { get; } = contextType ?? throw new ArgumentNullException(nameof(contextType));
}

/// <summary>Creates one resource owned by the built Agent rather than application DI.</summary>
public sealed record ToolHarnessAgentResourceDescriptor
{
    /// <summary>Gets the exact service contract exposed during middleware activation.</summary>
    public required Type ResourceType { get; init; }
    /// <summary>Gets the concrete implementation type used to detect conflicting declarations.</summary>
    public required Type ImplementationType { get; init; }
    /// <summary>Gets the concrete resource factory emitted by source generation.</summary>
    public required Func<object> Factory { get; init; }
}

/// <summary>Describes one activated middleware and its explicit disposal owner.</summary>
public sealed record ToolHarnessMiddlewareActivation
{
    private ToolHarnessMiddlewareActivation(
        IToolHarnessMiddleware middleware,
        ToolHarnessMiddlewareOwnership ownership)
    {
        Middleware = middleware;
        Ownership = ownership;
    }

    /// <summary>Gets the exact activated middleware instance.</summary>
    public IToolHarnessMiddleware Middleware { get; }

    /// <summary>Gets the component that must dispose the activated instance.</summary>
    public ToolHarnessMiddlewareOwnership Ownership { get; }

    /// <summary>Creates an execution-owned activation.</summary>
    public static ToolHarnessMiddlewareActivation ExecutionOwned(IToolHarnessMiddleware middleware) =>
        new(middleware ?? throw new ArgumentNullException(nameof(middleware)), ToolHarnessMiddlewareOwnership.Execution);

    /// <summary>Marks middleware resolved through the activation context's input child scope as services-owned.</summary>
    public static ToolHarnessMiddlewareActivation ServicesOwned(IToolHarnessMiddleware middleware) =>
        new(middleware ?? throw new ArgumentNullException(nameof(middleware)), ToolHarnessMiddlewareOwnership.Services);
}

/// <summary>Creates one middleware activation for an accepted input execution.</summary>
public delegate ToolHarnessMiddlewareActivation ToolHarnessMiddlewareFactory(ToolHarnessActivationContext context);

/// <summary>Generated immutable declaration for one ordered ToolHarness middleware position.</summary>
public sealed record ToolHarnessMiddlewareDescriptor
{
    /// <summary>Gets the exact declared middleware type.</summary>
    public required Type MiddlewareType { get; init; }

    /// <summary>Gets the reflection-free per-execution activation factory.</summary>
    public required ToolHarnessMiddlewareFactory Factory { get; init; }

    /// <summary>Gets the generated configuration type, when activation requires configuration.</summary>
    public Type? ConfigurationType { get; init; }
}

/// <summary>Runtime-only facts available while activating one ToolHarness pipeline.</summary>
public sealed class ToolHarnessActivationContext
{
    private readonly IReadOnlyDictionary<Type, object> _agentResources;
    private readonly IReadOnlyDictionary<Type, JsonElement> _configuration;
    private readonly Type? _middlewareType;
    private readonly string? _activationHook;
    private readonly HashSet<object> _resolvedServices = new(ReferenceEqualityComparer.Instance);

    internal ToolHarnessActivationContext(
        string harnessIdentity,
        string inputExecutionId,
        IServiceProvider? services,
        AgentRunConfig runConfig,
        IReadOnlyDictionary<Type, object>? agentResources = null,
        IReadOnlyDictionary<Type, JsonElement>? configuration = null,
        string? sessionId = null,
        string? threadId = null,
        string? canonicalWorkspaceIdentity = null,
        Type? middlewareType = null,
        string? activationHook = null)
    {
        HarnessIdentity = harnessIdentity;
        InputExecutionId = inputExecutionId;
        Services = services;
        RunConfig = runConfig;
        SessionId = sessionId;
        ThreadId = threadId;
        CanonicalWorkspaceIdentity = canonicalWorkspaceIdentity;
        _middlewareType = middlewareType;
        _activationHook = activationHook;
        _agentResources = agentResources ?? new Dictionary<Type, object>();
        _configuration = configuration ?? new Dictionary<Type, JsonElement>();
    }

    /// <summary>Gets the stable generated harness identity.</summary>
    public string HarnessIdentity { get; }
    /// <summary>Gets the unique accepted-input execution identity.</summary>
    public string InputExecutionId { get; }
    /// <summary>Gets the input child-scope provider, or null when DI is unavailable.</summary>
    public IServiceProvider? Services { get; }
    /// <summary>Gets the captured run configuration.</summary>
    public AgentRunConfig RunConfig { get; }
    /// <summary>Gets the session identity, when available.</summary>
    public string? SessionId { get; }
    /// <summary>Gets the thread identity, when available.</summary>
    public string? ThreadId { get; }
    internal string? CanonicalWorkspaceIdentity { get; }

    /// <summary>Gets required stable configuration without runtime constructor discovery.</summary>
    public T GetConfiguration<T>(JsonTypeInfo<T> typeInfo) where T : notnull
    {
        ArgumentNullException.ThrowIfNull(typeInfo);
        if (!_configuration.TryGetValue(typeof(T), out var value))
            throw Failure($"required configuration '{typeof(T).FullName}' is unavailable");
        return JsonSerializer.Deserialize(value, typeInfo) ??
            throw Failure($"required configuration '{typeof(T).FullName}' decoded to null");
    }

    /// <summary>Gets configured data or a generated declaration's explicit default value.</summary>
    public T GetConfigurationOrDefault<T>(JsonTypeInfo<T> typeInfo, Func<T> defaultFactory) where T : notnull
    {
        ArgumentNullException.ThrowIfNull(typeInfo);
        ArgumentNullException.ThrowIfNull(defaultFactory);
        return _configuration.TryGetValue(typeof(T), out var value)
            ? JsonSerializer.Deserialize(value, typeInfo) ?? throw Failure($"configuration '{typeof(T).FullName}' decoded to null")
            : defaultFactory();
    }

    /// <summary>Gets an optional child-scope service.</summary>
    public T? GetService<T>()
    {
        if (Services?.GetService(typeof(T)) is not T value)
            return default;
        lock (_resolvedServices) _resolvedServices.Add(value);
        return value;
    }

    /// <summary>Gets a required child-scope service and fails closed when no scope exists.</summary>
    public T GetRequiredService<T>() where T : notnull => GetService<T>() ??
        throw Failure($"required child-scope service '{typeof(T).FullName}' is unavailable");

    /// <summary>Resolves exact middleware from the input child scope and marks that scope as its disposal owner.</summary>
    /// <typeparam name="TMiddleware">The exact generated middleware type.</typeparam>
    /// <returns>An activation whose instance and disposal are both owned by the input child scope.</returns>
    public ToolHarnessMiddlewareActivation GetRequiredServicesOwned<TMiddleware>()
        where TMiddleware : class, IToolHarnessMiddleware =>
        ToolHarnessMiddlewareActivation.ServicesOwned(GetRequiredService<TMiddleware>());

    internal bool WasResolvedFromServices(object instance)
    {
        lock (_resolvedServices) return _resolvedServices.Contains(instance);
    }

    /// <summary>Gets a required resource owned by the built Agent.</summary>
    public T GetRequiredAgentResource<T>() where T : notnull =>
        _agentResources.TryGetValue(typeof(T), out var value) && value is T typed
            ? typed
            : throw Failure($"required Agent resource '{typeof(T).FullName}' is unavailable");

    /// <summary>Gets the canonical workspace identity required by workspace-owned middleware.</summary>
    public string GetCanonicalWorkspaceIdentity() =>
        !string.IsNullOrWhiteSpace(CanonicalWorkspaceIdentity)
            ? CanonicalWorkspaceIdentity
            : throw Failure("canonical workspace identity is unavailable");

    internal ToolHarnessActivationContext ForMiddleware(Type middlewareType, string activationHook) => new(
        HarnessIdentity, InputExecutionId, Services, RunConfig, _agentResources, _configuration,
        SessionId, ThreadId, CanonicalWorkspaceIdentity,
        middlewareType ?? throw new ArgumentNullException(nameof(middlewareType)),
        string.IsNullOrWhiteSpace(activationHook) ? throw new ArgumentException("Activation hook is required.", nameof(activationHook)) : activationHook);

    private InvalidOperationException Failure(string message) => new(
        $"ToolHarness '{HarnessIdentity}' middleware '{_middlewareType?.FullName ?? "<unbound>"}' " +
        $"activation hook '{_activationHook ?? "<unbound>"}' for input '{InputExecutionId}' failed: {message}.");
}

/// <summary>Reason supplied to deterministic ToolHarness middleware deactivation.</summary>
public enum ToolHarnessDeactivationReason
{
    /// <summary>The accepted execution completed successfully.</summary>
    Completed,
    /// <summary>The accepted execution failed.</summary>
    Failed,
    /// <summary>The accepted execution was cancelled.</summary>
    Cancelled,
    /// <summary>The owning Agent is shutting down.</summary>
    Shutdown
}

/// <summary>Runtime-only facts supplied during deterministic middleware deactivation.</summary>
public sealed record ToolHarnessDeactivationContext(
    /// <summary>Gets the stable generated ToolHarness identity.</summary>
    string HarnessIdentity,
    /// <summary>Gets the accepted input execution identity.</summary>
    string InputExecutionId,
    /// <summary>Gets the reason teardown began.</summary>
    ToolHarnessDeactivationReason Reason);

/// <summary>Optional activation/deactivation lifecycle for ToolHarness middleware.</summary>
public interface IToolHarnessMiddlewareLifecycle
{
    /// <summary>Runs after all declared instances are constructed, in declaration order.</summary>
    ValueTask OnHarnessActivatedAsync(ToolHarnessActivationContext context, CancellationToken cancellationToken);
    /// <summary>Runs before disposal, in reverse declaration order.</summary>
    ValueTask OnHarnessDeactivatingAsync(ToolHarnessDeactivationContext context, CancellationToken cancellationToken);
}
