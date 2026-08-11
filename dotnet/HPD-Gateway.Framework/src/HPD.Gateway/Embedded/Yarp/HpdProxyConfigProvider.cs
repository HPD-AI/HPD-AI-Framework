using System.Collections.Immutable;
using Microsoft.Extensions.Primitives;
using Yarp.ReverseProxy.Configuration;

namespace HPD.Gateway;

internal sealed class HpdProxyConfigProvider : IProxyConfigProvider, IDisposable
{
    private OwnedProxyConfig _current = OwnedProxyConfig.Empty();
    private bool _disposed;

    public IProxyConfig GetConfig() => Volatile.Read(ref _current);

    internal OwnedProxyConfig Prepare(GatewayPreparedApplication application)
    {
        GatewayLogicalGeneration generation = GatewayLogicalGeneration.Create(application);
        return new OwnedProxyConfig(
            generation.RevisionId,
            application.Routes,
            application.Plan.Dependencies.IsEmpty ? application.Clusters : application.Plan.Clusters,
            generation,
            application.NativeRevisionId);
    }

    internal void Install(OwnedProxyConfig next)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(next);
        var previous = Interlocked.Exchange(ref _current, next);
        previous.SignalChange();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Volatile.Read(ref _current).Dispose();
    }
}

internal sealed class OwnedProxyConfig : IProxyConfig, IDisposable
{
    private readonly CancellationTokenSource _change = new();

    internal OwnedProxyConfig(
        string revisionId,
        ImmutableArray<RouteConfig> routes,
        ImmutableArray<ClusterConfig> clusters,
        GatewayLogicalGeneration? logicalGeneration = null,
        string? nativeRevisionId = null)
    {
        RevisionId = revisionId;
        Routes = routes;
        Clusters = clusters;
        LogicalGeneration = logicalGeneration;
        NativeRevisionId = nativeRevisionId;
        ChangeToken = new CancellationChangeToken(_change.Token);
    }

    public string RevisionId { get; }
    public IReadOnlyList<RouteConfig> Routes { get; }
    public IReadOnlyList<ClusterConfig> Clusters { get; }
    public IChangeToken ChangeToken { get; }
    internal GatewayLogicalGeneration? LogicalGeneration { get; }
    internal string? NativeRevisionId { get; }

    internal static OwnedProxyConfig Empty() => new(
        $"hpd-bootstrap-{Guid.NewGuid():N}",
        [],
        []);

    internal void SignalChange() => _change.Cancel();

    public void Dispose() => _change.Dispose();
}

internal sealed record GatewayLogicalGeneration(
    string ApplicationId,
    ContentHash SymbolicPlanIdentity,
    ContentHash NativeGraphIdentity,
    ImmutableArray<string> RouteIds,
    ImmutableArray<string> ClusterIds)
{
    internal string RevisionId => $"hpd-runtime-v1-{ApplicationId}-{SymbolicPlanIdentity.Value}-{NativeGraphIdentity.Value}";

    internal static GatewayLogicalGeneration Create(GatewayPreparedApplication application) => new(
        application.ApplicationId,
        application.SymbolicPlanIdentity,
        application.NativeGraphIdentity,
        application.Routes.Select(static value => value.RouteId).Order(StringComparer.Ordinal).ToImmutableArray(),
        application.Clusters.Select(static value => value.ClusterId).Order(StringComparer.Ordinal).ToImmutableArray());

    internal bool Matches(IProxyConfig config)
    {
        try
        {
            if (RouteIds.IsEmpty && ClusterIds.IsEmpty)
                return StringComparer.Ordinal.Equals(config.RevisionId, RevisionId) &&
                    config.Routes.Count == 0 && config.Clusters.Count == 0 &&
                    GatewayRuntimeGraphIdentity.ComputeNativeGeneration(config.Routes, config.Clusters) == NativeGraphIdentity;
            if (!ExactResources(config.Routes, RouteIds, static value => value.RouteId) ||
                !ExactResources(config.Clusters, ClusterIds, static value => value.ClusterId) ||
                !config.Routes.All(value => HasGeneration(value.Metadata)) ||
                !config.Clusters.All(value => HasGeneration(value.Metadata)))
                return false;
            return GatewayRuntimeGraphIdentity.ComputeNativeGeneration(config.Routes, config.Clusters) == NativeGraphIdentity;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private bool HasGeneration(IReadOnlyDictionary<string, string>? metadata) =>
        metadata is not null &&
        metadata.TryGetValue(GatewayRuntimePlanner.ApplicationIdMetadata, out string? applicationId) &&
        StringComparer.Ordinal.Equals(applicationId, ApplicationId) &&
        metadata.TryGetValue(GatewayRuntimePlanner.SymbolicPlanIdentityMetadata, out string? planIdentity) &&
        StringComparer.Ordinal.Equals(planIdentity, SymbolicPlanIdentity.Value);

    private static bool ExactResources<T>(
        IReadOnlyList<T> resources,
        ImmutableArray<string> expected,
        Func<T, string> identity)
    {
        if (resources.Count != expected.Length) return false;
        string[] actual = resources.Select(identity).Order(StringComparer.Ordinal).ToArray();
        return actual.Distinct(StringComparer.Ordinal).Count() == actual.Length &&
            actual.SequenceEqual(expected, StringComparer.Ordinal);
    }
}
