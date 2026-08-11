using System.Collections.Immutable;
using System.Security.Cryptography;
using Yarp.ReverseProxy.Configuration;

namespace HPD.Gateway;

internal sealed record GatewayRuntimeDependencyBinding(
    string UpstreamId,
    DiscoveryProfileId Profile,
    ServiceDiscoveryName Service,
    ServiceDiscoveryEndpointName? Endpoint,
    ImmutableArray<ServiceDiscoveryScheme> Schemes,
    string? TlsServerName,
    DiscoveryStaleBehavior StaleBehavior,
    ContentHash CapabilityIdentity,
    int MaximumEndpoints);

internal sealed class GatewayRuntimePlan
{
    internal const int MaximumApplicationIdLength = 32;
    internal const int MaximumDependencies = 4096;
    internal const int MaximumResolvedEndpoints = 512;

    internal GatewayRuntimePlan(
        PublicationCandidateIdentity identity,
        ImmutableArray<RouteConfig> routes,
        ImmutableArray<ClusterConfig> clusters,
        ImmutableArray<GatewayRuntimeDependencyBinding> dependencies,
        GatewayEffectiveProjectionBuilder.PreparedProjection effective,
        string applicationId,
        ContentHash symbolicPlanIdentity)
    {
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(effective);
        if (routes.IsDefault || clusters.IsDefault || dependencies.IsDefault || dependencies.Length > MaximumDependencies)
            throw new ArgumentException("Runtime-plan collections must be initialized and bounded.");
        if (!effective.Routes.Equals(routes))
            throw new ArgumentException("Effective provenance must bind the exact symbolic Route array.", nameof(effective));
        if (dependencies.Select(static value => value.UpstreamId).Distinct(StringComparer.Ordinal).Count() != dependencies.Length ||
            !dependencies.Select(static value => value.UpstreamId).SequenceEqual(dependencies.Select(static value => value.UpstreamId).Order(StringComparer.Ordinal)))
            throw new ArgumentException("Runtime dependencies must be unique and ordinally sorted.", nameof(dependencies));

        Identity = identity;
        Routes = routes;
        Clusters = clusters;
        Dependencies = dependencies;
        Effective = effective;
        if (applicationId.Length != MaximumApplicationIdLength ||
            !applicationId.All(static value => value is >= '0' and <= '9' or >= 'a' and <= 'f'))
            throw new ArgumentException("Application identity is invalid.", nameof(applicationId));
        if (symbolicPlanIdentity.Algorithm != "sha-256" || symbolicPlanIdentity.Value is not { Length: 64 } planHash ||
            !planHash.All(static value => value is >= '0' and <= '9' or >= 'a' and <= 'f'))
            throw new ArgumentException("Symbolic-plan identity is invalid.", nameof(symbolicPlanIdentity));
        if (symbolicPlanIdentity != ComputeIdentity(identity, routes, clusters, dependencies, effective.Snapshot))
            throw new ArgumentException("Symbolic-plan identity does not match the exact planned behavior graph.", nameof(symbolicPlanIdentity));
        if (!ValidResources(routes, applicationId, symbolicPlanIdentity.Value) ||
            !ValidResources(clusters, applicationId, symbolicPlanIdentity.Value) ||
            dependencies.Any(dependency => !clusters.Any(cluster =>
                StringComparer.Ordinal.Equals(cluster.ClusterId, dependency.UpstreamId))))
            throw new ArgumentException("Runtime-plan resources are incomplete or not correlated to the plan identity.");
        ApplicationId = applicationId;
        SymbolicPlanIdentity = symbolicPlanIdentity;
    }

    internal PublicationCandidateIdentity Identity { get; }
    internal string ApplicationId { get; }
    internal ContentHash SymbolicPlanIdentity { get; }
    internal ImmutableArray<RouteConfig> Routes { get; }
    internal ImmutableArray<ClusterConfig> Clusters { get; }
    internal ImmutableArray<GatewayRuntimeDependencyBinding> Dependencies { get; }
    internal GatewayEffectiveProjectionBuilder.PreparedProjection Effective { get; }

    internal static string CreateApplicationId() => Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(16));

    internal static ContentHash ComputeIdentity(
        PublicationCandidateIdentity identity,
        ImmutableArray<RouteConfig> routes,
        ImmutableArray<ClusterConfig> clusters,
        ImmutableArray<GatewayRuntimeDependencyBinding> dependencies,
        GatewayPreparedProjectionSnapshot effectiveSnapshot) =>
        GatewayRuntimeGraphIdentity.ComputePlan(identity, routes, clusters, dependencies, effectiveSnapshot);

    private static bool ValidResources(
        ImmutableArray<RouteConfig> routes,
        string applicationId,
        string planIdentity) =>
        routes.Length <= 10_000 &&
        routes.Select(static value => value.RouteId).Distinct(StringComparer.Ordinal).Count() == routes.Length &&
        routes.All(route => route.Metadata is not null &&
            route.Metadata.TryGetValue(GatewayRuntimePlanner.ApplicationIdMetadata, out string? application) && application == applicationId &&
            route.Metadata.TryGetValue(GatewayRuntimePlanner.SymbolicPlanIdentityMetadata, out string? plan) && plan == planIdentity);

    private static bool ValidResources(
        ImmutableArray<ClusterConfig> clusters,
        string applicationId,
        string planIdentity) =>
        clusters.Length <= 10_000 &&
        clusters.Select(static value => value.ClusterId).Distinct(StringComparer.Ordinal).Count() == clusters.Length &&
        clusters.All(cluster => cluster.Metadata is not null &&
            cluster.Metadata.TryGetValue(GatewayRuntimePlanner.ApplicationIdMetadata, out string? application) && application == applicationId &&
            cluster.Metadata.TryGetValue(GatewayRuntimePlanner.SymbolicPlanIdentityMetadata, out string? plan) && plan == planIdentity);
}
