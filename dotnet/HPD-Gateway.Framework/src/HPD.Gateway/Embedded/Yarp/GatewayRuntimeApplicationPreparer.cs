using System.Collections.Immutable;
using Yarp.ReverseProxy.Configuration;

namespace HPD.Gateway;

internal sealed class GatewayRuntimeApplicationPreparer(IConfigValidator nativeValidator)
{
    private const int MaximumDiagnostics = 256;
    private static readonly object NativeValidationKey = new();
    private readonly IConfigValidator _nativeValidator = nativeValidator;

    internal sealed class NativeValidationReceipt
    {
        internal NativeValidationReceipt(
            GatewayRuntimePlan plan,
            ImmutableArray<ClusterConfig> clusters,
            ImmutableArray<GatewayPreparedDependencyResolution> resolutions,
            object key)
        {
            if (!ReferenceEquals(key, NativeValidationKey))
                throw new InvalidOperationException("Native-validation receipts may only be minted by the runtime application preparer.");
            Plan = plan;
            Clusters = clusters;
            Resolutions = resolutions;
        }

        internal GatewayRuntimePlan Plan { get; }
        internal ImmutableArray<ClusterConfig> Clusters { get; }
        internal ImmutableArray<GatewayPreparedDependencyResolution> Resolutions { get; }
    }

    internal async ValueTask<(GatewayPreparedApplication? Application, ImmutableArray<GatewayRuntimePlanningDiagnostic> Diagnostics)> PrepareAsync(
        GatewayRuntimePlan plan,
        string nativeRevisionId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (!plan.Dependencies.IsEmpty)
            return (null, [new("preparation.dependencies-unresolved", "$", "The governed destination resolver has not prepared the symbolic dependencies.")]);
        return await PrepareAsync(plan, plan.Clusters, [], nativeRevisionId, cancellationToken).ConfigureAwait(false);
    }

    internal async ValueTask<(GatewayPreparedApplication? Application, ImmutableArray<GatewayRuntimePlanningDiagnostic> Diagnostics)> PrepareAsync(
        GatewayRuntimePlan plan,
        ImmutableArray<ClusterConfig> resolvedClusters,
        ImmutableArray<GatewayPreparedDependencyResolution> resolutions,
        string nativeRevisionId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ImmutableArray<ClusterConfig> frozenClusters;
        try
        {
            frozenClusters = FreezeResolvedClusters(plan, resolvedClusters);
            GatewayPreparedApplication.ValidateResolvedGraph(plan, frozenClusters, resolutions);
        }
        catch (Exception)
        {
            return (null, [new("preparation.application-invalid", "$", "The exact resolved application graph is invalid.")]);
        }

        var diagnostics = ImmutableArray.CreateBuilder<GatewayRuntimePlanningDiagnostic>();
        foreach (ClusterConfig cluster in frozenClusters)
        {
            if (cancellationToken.IsCancellationRequested)
                return Canceled();
            try
            {
                if ((await _nativeValidator.ValidateClusterAsync(cluster).ConfigureAwait(false)).Count > 0)
                    Add(diagnostics, "native.cluster-validation-failed", $"upstreams[id={cluster.ClusterId}]", "YARP rejected the prepared Cluster configuration.");
            }
            catch (Exception)
            {
                Add(diagnostics, "native.cluster-validation-failed", $"upstreams[id={cluster.ClusterId}]", "YARP Cluster validation failed unexpectedly.");
            }
        }
        foreach (RouteConfig route in plan.Routes)
        {
            if (cancellationToken.IsCancellationRequested)
                return Canceled();
            try
            {
                if ((await _nativeValidator.ValidateRouteAsync(route).ConfigureAwait(false)).Count > 0)
                    Add(diagnostics, "native.route-validation-failed", $"routes[id={route.RouteId}]", "YARP rejected the prepared Route configuration.");
            }
            catch (Exception)
            {
                Add(diagnostics, "native.route-validation-failed", $"routes[id={route.RouteId}]", "YARP Route validation failed unexpectedly.");
            }
        }
        if (diagnostics.Count > 0) return (null, diagnostics.ToImmutable());
        if (cancellationToken.IsCancellationRequested) return Canceled();
        try
        {
            var receipt = new NativeValidationReceipt(plan, frozenClusters, resolutions, NativeValidationKey);
            return (GatewayPreparedApplication.Create(receipt, nativeRevisionId), []);
        }
        catch (Exception)
        {
            return (null, [new("preparation.application-invalid", "$", "The exact prepared application is invalid.")]);
        }

        static (GatewayPreparedApplication?, ImmutableArray<GatewayRuntimePlanningDiagnostic>) Canceled() =>
            (null, [new("preparation.canceled", "$", "Preparation was canceled before native validation completed.")]);
    }

    private static ImmutableArray<ClusterConfig> FreezeResolvedClusters(
        GatewayRuntimePlan plan,
        ImmutableArray<ClusterConfig> resolvedClusters)
    {
        if (resolvedClusters.IsDefault || resolvedClusters.Length != plan.Clusters.Length)
            return resolvedClusters;
        if (plan.Dependencies.IsEmpty)
            return resolvedClusters;

        var dependencies = plan.Dependencies
            .Select(static dependency => dependency.UpstreamId)
            .ToImmutableHashSet(StringComparer.Ordinal);
        var frozen = ImmutableArray.CreateBuilder<ClusterConfig>(resolvedClusters.Length);
        foreach (ClusterConfig cluster in resolvedClusters)
        {
            if (!dependencies.Contains(cluster.ClusterId))
            {
                frozen.Add(cluster);
                continue;
            }

            IReadOnlyDictionary<string, DestinationConfig>? destinations = cluster.Destinations;
            if (destinations is null)
            {
                frozen.Add(cluster);
                continue;
            }

            ImmutableDictionary<string, DestinationConfig> frozenDestinations = destinations
                .ToImmutableDictionary(
                    static pair => pair.Key,
                    static pair => pair.Value with
                    {
                        Metadata = pair.Value.Metadata?.ToImmutableDictionary(StringComparer.Ordinal),
                    },
                    StringComparer.Ordinal);
            frozen.Add(cluster with { Destinations = frozenDestinations });
        }
        return frozen.MoveToImmutable();
    }

    private static void Add(
        ImmutableArray<GatewayRuntimePlanningDiagnostic>.Builder diagnostics,
        string code,
        string path,
        string message)
    {
        if (diagnostics.Count < MaximumDiagnostics)
            diagnostics.Add(new(code, path, message));
    }
}
