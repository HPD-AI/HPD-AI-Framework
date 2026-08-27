using Microsoft.Extensions.AI;

namespace HPD.Agent;

/// <summary>
/// Result from loading an OpenAPI source. Contains the generated functions and any
/// HttpClients that were created internally (not user-provided) that must be disposed
/// with the Agent.
/// </summary>
internal sealed class OpenApiLoadResult
{
    public List<AIFunction> Functions { get; init; } = [];

    /// <summary>
    /// HttpClients created by the loader for sources that did not provide their own.
    /// These are owned by the caller (Agent) and must be disposed when the Agent is disposed.
    /// User-provided HttpClients (config.HttpClient != null) are NOT included here.
    /// </summary>
    public List<HttpClient> OwnedHttpClients { get; init; } = [];
}

/// <summary>
/// Indirection interface allowing HPD-Agent.OpenApi to register its loading
/// implementation into AgentBuilder without creating a direct dependency from core
/// to the extension library. Same pattern as provider modules.
///
/// Registered via [ModuleInitializer] in HPD-Agent.OpenApi's OpenApiAutoDiscovery.
///
/// The loader owns all config interpretation, HttpClient lifecycle decisions, and
/// function creation. AgentBuilder passes raw registrations through and collects the results.
/// </summary>
internal interface IOpenApiLoader
{
    /// <summary>
    /// Loads all OpenAPI sources and returns the generated functions plus any
    /// internally-created HttpClients that must be disposed with the Agent.
    /// </summary>
    Task<OpenApiLoadResult> LoadAllAsync(
        IReadOnlyList<OpenApiSourceRegistration> sources,
        CancellationToken cancellationToken);
}

/// <summary>Adapts one optional OpenAPI registration into the capability-source lifetime model.</summary>
internal sealed class OpenApiCapabilitySourceFactory(
    CapabilitySourceId id,
    OpenApiSourceRegistration registration,
    IOpenApiLoader loader) : IAgentCapabilitySourceFactory
{
    public CapabilitySourceId Id { get; } = id;

    public ValueTask<IAgentCapabilitySource> CreateAsync(
        IServiceProvider? services,
        CancellationToken cancellationToken) =>
        ValueTask.FromResult<IAgentCapabilitySource>(new OpenApiCapabilitySource(Id, registration, loader));
}

internal sealed class OpenApiCapabilitySource(
    CapabilitySourceId id,
    OpenApiSourceRegistration registration,
    IOpenApiLoader loader) : IAgentCapabilitySource
{
    private long _revision = -1;
    private int _disposed;
    public CapabilitySourceId Id { get; } = id;

    public async ValueTask<CapabilitySourceLoadResult> LoadAsync(
        CapabilityLoadContext context,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        var result = await loader.LoadAllAsync([registration], cancellationToken).ConfigureAwait(false);
        var revision = CapabilitySourceRevision.Create(Interlocked.Increment(ref _revision));
        return new(new OpenApiCapabilityRevisionOwner(Id, revision, result));
    }

    public async IAsyncEnumerable<CapabilityInvalidation> WatchAsync(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await Task.CompletedTask;
        yield break;
    }

    public ValueTask DisposeAsync()
    {
        Interlocked.Exchange(ref _disposed, 1);
        return ValueTask.CompletedTask;
    }
}

internal sealed class OpenApiCapabilityRevisionOwner : ICapabilitySourceRevisionOwner
{
    private readonly MaterializedCapabilityRevisionOwner _functions;
    private List<HttpClient>? _clients;

    internal OpenApiCapabilityRevisionOwner(
        CapabilitySourceId sourceId,
        CapabilitySourceRevision revision,
        OpenApiLoadResult result)
    {
        SourceId = sourceId;
        Revision = revision;
        _functions = new MaterializedCapabilityRevisionOwner(sourceId, revision, result.Functions);
        _clients = result.OwnedHttpClients;
    }

    public CapabilitySourceId SourceId { get; }
    public CapabilitySourceRevision Revision { get; }
    public CapabilitySourceSnapshot Snapshot => _functions.Snapshot;

    public async ValueTask DisposeAsync()
    {
        await _functions.DisposeAsync().ConfigureAwait(false);
        var clients = Interlocked.Exchange(ref _clients, null);
        if (clients is null) return;
        foreach (var client in clients) client.Dispose();
    }
}

/// <summary>
/// A pending OpenAPI source registered via WithOpenApi() or [OpenApi] toolharness attribute.
/// Stored as a plain data record in core — no HPD.OpenApi.Core types referenced here.
/// Config is stored as <see cref="object"/> and cast to OpenApiConfig inside HPD-Agent.OpenApi.
/// </summary>
internal sealed record OpenApiSourceRegistration(
    /// <summary>Display name / prefix for the OpenAPI source.</summary>
    string Name,

    /// <summary>Parent toolharness container name, or null for standalone WithOpenApi() sources.</summary>
    string? ParentContainer,

    /// <summary>
    /// When true, functions are wrapped behind a nested container inside the parent toolharness.
    /// When false (default), functions appear directly under the parent toolharness.
    /// Read from OpenApiConfig.CollapseWithinToolHarness after casting.
    /// </summary>
    bool CollapseWithinToolHarness,

    /// <summary>
    /// The OpenApiConfig instance stored as object to avoid a dependency on HPD-Agent.OpenApi.
    /// Cast to OpenApiConfig inside OpenApiLoader.LoadAllAsync.
    /// </summary>
    object Config
);
