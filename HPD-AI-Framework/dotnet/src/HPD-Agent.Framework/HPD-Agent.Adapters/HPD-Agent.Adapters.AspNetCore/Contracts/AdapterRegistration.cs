using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;

namespace HPD.Agent.Adapters.AspNetCore;

/// <summary>
/// AOT-safe descriptor for a registered platform adapter.
/// One entry is generated per <see cref="HpdAdapterAttribute"/> class by the source generator
/// and collected in the assembly-scoped <c>AdapterRegistry.All</c> array.
/// </summary>
/// <param name="Name">Lowercase adapter identifier (e.g. "slack").</param>
/// <param name="AdapterType">The concrete adapter class type.</param>
/// <param name="MapEndpoint">
/// Delegate that maps the adapter's webhook endpoint onto an <see cref="IEndpointRouteBuilder"/>.
/// The <c>path</c> parameter overrides the default path when non-null.
/// </param>
/// <param name="DefaultPath">Default webhook path (e.g. "/webhooks/slack").</param>
public sealed record AdapterRegistration(
    string Name,
    Type AdapterType,
    Func<IEndpointRouteBuilder, string?, IEndpointConventionBuilder> MapEndpoint,
    string DefaultPath);
