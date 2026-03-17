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

/// <summary>
/// A pending OpenAPI source registered via WithOpenApi() or [OpenApi] toolkit attribute.
/// Stored as a plain data record in core — no HPD.OpenApi.Core types referenced here.
/// Config is stored as <see cref="object"/> and cast to OpenApiConfig inside HPD-Agent.OpenApi.
/// </summary>
internal sealed record OpenApiSourceRegistration(
    /// <summary>Display name / prefix for the OpenAPI source.</summary>
    string Name,

    /// <summary>Parent toolkit container name, or null for standalone WithOpenApi() sources.</summary>
    string? ParentContainer,

    /// <summary>
    /// When true, functions are wrapped behind a nested container inside the parent toolkit.
    /// When false (default), functions appear directly under the parent toolkit.
    /// Read from OpenApiConfig.CollapseWithinToolkit after casting.
    /// </summary>
    bool CollapseWithinToolkit,

    /// <summary>
    /// The OpenApiConfig instance stored as object to avoid a dependency on HPD-Agent.OpenApi.
    /// Cast to OpenApiConfig inside OpenApiLoader.LoadAllAsync.
    /// </summary>
    object Config
);
