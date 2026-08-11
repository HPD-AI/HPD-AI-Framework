using System.Collections.Immutable;
using System.Reflection;
using HPD.Gateway;
using Yarp.ReverseProxy.Configuration;

namespace HPD.Gateway.Tests;

internal static class PreparedApplicationTestFactory
{
    internal static GatewayPreparedApplication Create(
        PublicationCandidateIdentity identity,
        ImmutableArray<RouteConfig> routes,
        ImmutableArray<ClusterConfig> clusters,
        string nativeRevisionId,
        GatewayPreparedProjectionSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (snapshot.Records.IsDefault)
            throw new ArgumentException("Effective records must be initialized.", nameof(snapshot));
        ImmutableArray<GatewayRuntimeDependencyBinding> dependencies = [];
        string applicationId = GatewayRuntimePlan.CreateApplicationId();
        ContentHash planIdentity = new("sha-256", new string('0', 64));
        routes = routes.Select(route => route with
        {
            Metadata = (route.Metadata ?? ImmutableDictionary<string, string>.Empty).ToImmutableDictionary(StringComparer.Ordinal)
                .SetItem(GatewayRuntimePlanner.ApplicationIdMetadata, applicationId)
                .SetItem(GatewayRuntimePlanner.SymbolicPlanIdentityMetadata, planIdentity.Value),
        }).ToImmutableArray();
        clusters = clusters.Select(cluster => cluster with
        {
            Metadata = (cluster.Metadata ?? ImmutableDictionary<string, string>.Empty).ToImmutableDictionary(StringComparer.Ordinal)
                .SetItem(GatewayRuntimePlanner.ApplicationIdMetadata, applicationId)
                .SetItem(GatewayRuntimePlanner.SymbolicPlanIdentityMetadata, planIdentity.Value),
        }).ToImmutableArray();
        var constructor = typeof(GatewayEffectiveProjectionBuilder.PreparedProjection)
            .GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic)
            .Single();
        var key = typeof(GatewayEffectiveProjectionBuilder)
            .GetField("PreparationKey", BindingFlags.Static | BindingFlags.NonPublic)!
            .GetValue(null)!;
        planIdentity = GatewayRuntimePlan.ComputeIdentity(identity, routes, clusters, dependencies, snapshot);
        routes = routes.Select(route => route with
        {
            Metadata = route.Metadata!.ToImmutableDictionary(StringComparer.Ordinal)
                .SetItem(GatewayRuntimePlanner.SymbolicPlanIdentityMetadata, planIdentity.Value),
        }).ToImmutableArray();
        clusters = clusters.Select(cluster => cluster with
        {
            Metadata = cluster.Metadata!.ToImmutableDictionary(StringComparer.Ordinal)
                .SetItem(GatewayRuntimePlanner.SymbolicPlanIdentityMetadata, planIdentity.Value),
        }).ToImmutableArray();
        var prepared = (GatewayEffectiveProjectionBuilder.PreparedProjection)constructor.Invoke([snapshot, routes, key]);
        var plan = new GatewayRuntimePlan(
            identity,
            routes,
            clusters,
            dependencies,
            prepared,
            applicationId,
            planIdentity);
        var validationConstructor = typeof(GatewayRuntimeApplicationPreparer.NativeValidationReceipt)
            .GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic)
            .Single();
        var validationKey = typeof(GatewayRuntimeApplicationPreparer)
            .GetField("NativeValidationKey", BindingFlags.Static | BindingFlags.NonPublic)!
            .GetValue(null)!;
        var validation = (GatewayRuntimeApplicationPreparer.NativeValidationReceipt)validationConstructor.Invoke(
            [plan, clusters, ImmutableArray<GatewayPreparedDependencyResolution>.Empty, validationKey]);
        return GatewayPreparedApplication.Create(validation, nativeRevisionId);
    }
}
