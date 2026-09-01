using System.Collections.Concurrent;

namespace HPD.AI.Platform.Studio;

/// <summary>Retains a bounded short-lived bootstrap authority for exact Runtime method dispatch.</summary>
public sealed class BaseStudioRuntimeLeaseRegistry
{
    private const int MaximumLeases = 256;
    private readonly ConcurrentDictionary<string, Lease> _leases = new(StringComparer.Ordinal);
    private readonly TimeProvider _timeProvider;
    /// <summary>Initializes the bounded registry with the host clock.</summary>
    public BaseStudioRuntimeLeaseRegistry(TimeProvider timeProvider)
        => _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));

    /// <summary>Publishes one immutable bootstrap authority after its response is successfully constructed.</summary>
    public void Publish(BaseStudioBootstrapInvocation invocation, BaseStudioBootstrapSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(invocation); ArgumentNullException.ThrowIfNull(snapshot);
        RequireFrozenGraphCorrespondence(invocation.ApplicationGraph, snapshot);
        DateTimeOffset now = _timeProvider.GetUtcNow();
        foreach ((string expiredKey, Lease value) in _leases)
            if (value.Snapshot.ExpiresAtUtc <= now) _leases.TryRemove(expiredKey, out _);
        if (_leases.Count >= MaximumLeases)
            throw new InvalidOperationException("base.studio.runtimeLeaseCapacityExceeded");
        string key = Hex(snapshot.SnapshotChecksum);
        _leases[key] = new(invocation.Request, snapshot,
            BaseStudioSha256.FromDigest(invocation.Authorization.Session.DescriptorChecksum.ToArray()));
    }

    /// <summary>Resolves a lease only for the exact session, graph, and unexpired response authority.</summary>
    public bool TryResolve(string? checksum, BaseStudioTransportAuthorization authorization,
        BaseStudioApplicationGraph graph, out BaseStudioBootstrapRequest request, out BaseStudioBootstrapSnapshot snapshot)
    {
        request = null!; snapshot = null!;
        if (checksum is null || checksum.Length != 64 || !checksum.All(static value => value is >= '0' and <= '9' or >= 'a' and <= 'f') ||
            !_leases.TryGetValue(checksum, out Lease? lease)) return false;
        if (lease.Snapshot.ExpiresAtUtc <= _timeProvider.GetUtcNow() ||
            lease.Snapshot.Authority.PrincipalGeneration != authorization.Session.PrincipalGeneration ||
            !BaseStudioSha256.FixedTimeEquals(lease.Snapshot.Authority.AuthenticatedSessionChecksum, authorization.Session.SessionChecksum) ||
            !BaseStudioSha256.FixedTimeEquals(lease.Snapshot.Authority.ProtectedScopeChecksum, authorization.Session.ProtectedScopeChecksum) ||
            !BaseStudioSha256.FixedTimeEquals(lease.DescriptorChecksum, authorization.Session.DescriptorChecksum) ||
            lease.Snapshot.Authority.ApplicationGraphGeneration != graph.Generation ||
            lease.Snapshot.Authority.StudioOwnerGeneration != graph.Generation ||
            !BaseStudioSha256.FixedTimeEquals(lease.Snapshot.Authority.ApplicationGraphChecksum, graph.Checksum))
        { _leases.TryRemove(checksum, out _); return false; }
        request = lease.Request; snapshot = lease.Snapshot; return true;
    }

    private static string Hex(BaseStudioSha256 checksum) => Convert.ToHexString(checksum.ToArray()).ToLowerInvariant();

    private static void RequireFrozenGraphCorrespondence(BaseStudioApplicationGraph graph, BaseStudioBootstrapSnapshot snapshot)
    {
        foreach (BaseStudioVisiblePage visible in snapshot.Pages)
        {
            BaseStudioModuleRegistration? module = graph.Modules.SingleOrDefault(value =>
                StringComparer.Ordinal.Equals(value.Identity.ModuleId, visible.ModuleId));
            BaseStudioPageRegistration? page = module?.Pages.SingleOrDefault(value =>
                StringComparer.Ordinal.Equals(value.PageId, visible.PageId));
            if (page is null || page.Version != visible.Version || page.Area != visible.Area ||
                page.Presentation.NavigationRole != visible.NavigationRole ||
                !BaseStudioSha256.FixedTimeEquals(page.Presentation.Checksum, visible.Presentation.Checksum) ||
                !BaseStudioSha256.FixedTimeEquals(page.Route.Checksum, visible.Route.Checksum) ||
                !page.AcceptedResources.SequenceEqual(visible.AcceptedResources) ||
                !BaseStudioSha256.FixedTimeEquals(page.Checksum, visible.RegistrationChecksum) ||
                !VisibleViewsMatch(module!, page, visible.Views) ||
                visible.InitialResource is not null &&
                (!StringComparer.Ordinal.Equals(visible.InitialResource.ApplicationId, graph.ApplicationId) ||
                 !page.AcceptedResources.Contains(visible.InitialResource.Kind)))
                throw new InvalidOperationException("base.studio.bootstrapGraphMismatch");
        }
    }

    private static bool VisibleViewsMatch(BaseStudioModuleRegistration module, BaseStudioPageRegistration page,
        System.Collections.Immutable.ImmutableArray<BaseStudioVisibleView> visible)
    {
        string[] expectedIds = page.Presentation.Sections.SelectMany(static value => value.ViewIds)
            .Order(StringComparer.Ordinal).ToArray();
        if (!expectedIds.SequenceEqual(visible.Select(static value => value.ViewId), StringComparer.Ordinal)) return false;
        foreach (BaseStudioVisibleView item in visible)
        {
            BaseStudioViewRegistration? expected = module.Views.SingleOrDefault(value =>
                StringComparer.Ordinal.Equals(value.ViewId, item.ViewId));
            if (expected is null || expected.Version != item.Version || expected.ItemKind != item.ItemKind ||
                !StringComparer.Ordinal.Equals(expected.ItemNodeId, item.ItemNodeId) ||
                !BaseStudioSha256.FixedTimeEquals(expected.ItemNodeChecksum, item.ItemNodeChecksum) ||
                !BaseStudioSha256.FixedTimeEquals(expected.Presentation.Checksum, item.Presentation.Checksum) ||
                !BaseStudioSha256.FixedTimeEquals(expected.Checksum, item.RegistrationChecksum)) return false;
        }
        return true;
    }
    private sealed record Lease(BaseStudioBootstrapRequest Request, BaseStudioBootstrapSnapshot Snapshot,
        BaseStudioSha256 DescriptorChecksum);
}

/// <summary>Validates module-owned response authority immediately before a retained bootstrap lease is reused.</summary>
public interface IBaseStudioResponseAuthorityValidator
{
    /// <summary>Returns whether the response authority still matches every current owner generation.</summary>
    ValueTask<bool> IsCurrentAsync(BaseStudioResponseAuthority authority, CancellationToken cancellationToken);
}
