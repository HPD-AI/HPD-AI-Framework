using System.Collections.Immutable;
using System.Reflection;
using HPD.Gateway.Effective;
using HPD.Gateway.Yarp;
using Yarp.ReverseProxy.Configuration;

namespace HPD.Gateway.Tests;

internal static class NativeBundleTestFactory
{
    internal static NativePublicationBundle Create(
        PublicationCandidateIdentity identity,
        ImmutableArray<RouteConfig> routes,
        ImmutableArray<ClusterConfig> clusters,
        string nativeRevisionId,
        GatewayEffectiveSnapshot snapshot)
    {
        var constructor = typeof(GatewayEffectiveProjectionBuilder.PreparedProjection)
            .GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic)
            .Single();
        var key = typeof(GatewayEffectiveProjectionBuilder)
            .GetField("PreparationKey", BindingFlags.Static | BindingFlags.NonPublic)!
            .GetValue(null)!;
        var prepared = (GatewayEffectiveProjectionBuilder.PreparedProjection)constructor.Invoke([snapshot, routes, key]);
        return NativePublicationBundle.Create(identity, routes, clusters, nativeRevisionId, prepared);
    }
}
