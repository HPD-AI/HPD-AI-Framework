using System.Collections.Immutable;

namespace HPD.AI.Platform.Studio;

/// <summary>Identifies the server-enforced Studio operating mode.</summary>
public enum StudioMode : byte { Inspect = 1, Operate }
/// <summary>Identifies one browser capability admitted by bootstrap.</summary>
public enum StudioBrowserCapability : byte { History = 1, ModuleScripts, WebSocket, ResizeObserver, AbortSignal }

/// <summary>Describes the exact static browser build requesting bootstrap.</summary>
public sealed class StudioBootstrapRequest
{
    private StudioBootstrapRequest(StudioSha256 shell, StudioSha256 assets, StudioSha256 client,
        string locale, ImmutableArray<StudioBrowserCapability> capabilities)
    { ShellContractChecksum = shell; EditionAssetGraphChecksum = assets; RuntimeClientChecksum = client; Locale = locale; ClientCapabilities = capabilities; }
    /// <summary>Gets the static shell ABI checksum.</summary>
    public StudioSha256 ShellContractChecksum { get; }
    /// <summary>Gets the authorization-neutral edition asset checksum.</summary>
    public StudioSha256 EditionAssetGraphChecksum { get; }
    /// <summary>Gets the runtime L41 interpreter checksum.</summary>
    public StudioSha256 RuntimeClientChecksum { get; }
    /// <summary>Gets the bounded BCP-47 locale.</summary>
    public string Locale { get; }
    /// <summary>Gets browser capabilities in discriminator order.</summary>
    public ImmutableArray<StudioBrowserCapability> ClientCapabilities { get; }

    /// <summary>Creates a deeply owned bootstrap request.</summary>
    public static StudioBootstrapRequest Create(StudioSha256 shell, StudioSha256 assets,
        StudioSha256 client, string locale, IEnumerable<StudioBrowserCapability> capabilities)
    {
        ArgumentNullException.ThrowIfNull(shell); ArgumentNullException.ThrowIfNull(assets); ArgumentNullException.ThrowIfNull(client);
        ArgumentException.ThrowIfNullOrWhiteSpace(locale);
        if (locale.Length > 35 || !locale.All(static value => char.IsAsciiLetterOrDigit(value) || value == '-'))
            throw new ArgumentException("Studio locale is invalid.", nameof(locale));
        ImmutableArray<StudioBrowserCapability> owned = StudioContractValidation.Materialize(capabilities, 5, false, nameof(capabilities));
        if (owned.Any(static value => !Enum.IsDefined(value)) || owned.Distinct().Count() != owned.Length ||
            !owned.SequenceEqual(owned.OrderBy(static value => (byte)value)))
            throw new ArgumentException("Studio browser capabilities are not canonical.", nameof(capabilities));
        return new(StudioSha256.FromBytes(shell.ToArray()), StudioSha256.FromBytes(assets.ToArray()),
            StudioSha256.FromBytes(client.ToArray()), new string(locale.AsSpan()), owned);
    }
}

/// <summary>Projects one module disclosed to the current principal.</summary>
public sealed record StudioVisibleModule(string ModuleId, int Version, string DisplayNameMessageId,
    StudioModuleNecessity Necessity, StudioSha256 RegistrationChecksum,
    StudioSha256 FrontendAbiChecksum, StudioSha256 AssetGraphChecksum);
/// <summary>Binds one authorized view's frozen presentation to its exact observation method.</summary>
public sealed record StudioVisibleView(string ViewId, int Version, string ObservationMethodId,
    StudioResourceKind ItemKind, string ItemNodeId, StudioSha256 ItemNodeChecksum,
    StudioViewPresentationRegistration Presentation, StudioSha256 RegistrationChecksum);
/// <summary>Projects one page disclosed to the current principal.</summary>
public sealed class StudioVisiblePage
{
    private StudioVisiblePage(string moduleId, string pageId, int version, StudioArea area,
        StudioNavigationRole navigationRole, StudioRouteTemplate route,
        ImmutableArray<StudioResourceKind> acceptedResources, ImmutableArray<string> observationMethodIds,
        ImmutableArray<string> resolverMethodIds, StudioResourceIdentity? initialResource,
        StudioPagePresentationRegistration presentation, ImmutableArray<StudioVisibleView> views,
        StudioSha256 registrationChecksum)
    { ModuleId = moduleId; PageId = pageId; Version = version; Area = area; NavigationRole = navigationRole;
      Route = route; AcceptedResources = acceptedResources; ObservationMethodIds = observationMethodIds;
      ResolverMethodIds = resolverMethodIds; InitialResource = initialResource; Presentation = presentation;
      Views = views; RegistrationChecksum = registrationChecksum; }
    /// <summary>Gets the owning module identity.</summary>
    public string ModuleId { get; }
    /// <summary>Gets the exact page identity.</summary>
    public string PageId { get; }
    /// <summary>Gets the positive page version.</summary>
    public int Version { get; }
    /// <summary>Gets the navigation area.</summary>
    public StudioArea Area { get; }
    /// <summary>Gets the navigation role.</summary>
    public StudioNavigationRole NavigationRole { get; }
    /// <summary>Gets the typed route template.</summary>
    public StudioRouteTemplate Route { get; }
    /// <summary>Gets the exact outward resource kinds accepted by this page.</summary>
    public ImmutableArray<StudioResourceKind> AcceptedResources { get; }
    /// <summary>Gets the registered page-observation methods in canonical identity order.</summary>
    public ImmutableArray<string> ObservationMethodIds { get; }
    /// <summary>Gets the registered resource-resolver methods in canonical identity order.</summary>
    public ImmutableArray<string> ResolverMethodIds { get; }
    /// <summary>Gets the server-issued initial resource for a landing page, or <see langword="null"/> for resource-parameter pages.</summary>
    public StudioResourceIdentity? InitialResource { get; }
    /// <summary>Gets the exact frozen page presentation.</summary>
    public StudioPagePresentationRegistration Presentation { get; }
    /// <summary>Gets page-owned view presentations bound to exact observation methods.</summary>
    public ImmutableArray<StudioVisibleView> Views { get; }
    /// <summary>Gets the graph-owned page registration checksum.</summary>
    public StudioSha256 RegistrationChecksum { get; }

    /// <summary>Creates a deeply owned executable page projection.</summary>
    public static StudioVisiblePage Create(string moduleId, string pageId, int version, StudioArea area,
        StudioNavigationRole navigationRole, StudioRouteTemplate route,
        IEnumerable<StudioResourceKind> acceptedResources, IEnumerable<string> observationMethodIds,
        IEnumerable<string> resolverMethodIds, StudioResourceIdentity? initialResource,
        StudioPagePresentationRegistration presentation, IEnumerable<StudioVisibleView> views,
        StudioSha256 registrationChecksum)
    {
        StudioContractValidation.Id(moduleId); StudioContractValidation.Id(pageId); StudioContractValidation.Enum(area);
        StudioContractValidation.Enum(navigationRole); ArgumentNullException.ThrowIfNull(route);
        ArgumentNullException.ThrowIfNull(registrationChecksum); ArgumentNullException.ThrowIfNull(presentation);
        if (version < 1) throw new ArgumentOutOfRangeException(nameof(version));
        ImmutableArray<StudioResourceKind> resources = StudioContractValidation.Materialize(acceptedResources, 64, true, nameof(acceptedResources));
        if (resources.Any(static value => !Enum.IsDefined(value)) || resources.Distinct().Count() != resources.Length ||
            !resources.SequenceEqual(resources.OrderBy(static value => (byte)value)))
            throw new ArgumentException("Studio page resources are not canonical.", nameof(acceptedResources));
        ImmutableArray<string> observations = StudioContractValidation.Ids(observationMethodIds, 256, true, nameof(observationMethodIds));
        ImmutableArray<string> resolvers = StudioContractValidation.Ids(resolverMethodIds, 64, resources.IsEmpty, nameof(resolverMethodIds));
        ImmutableArray<StudioVisibleView> ownedViews = StudioContractValidation.Materialize(views, 256, true, nameof(views));
        if (resolvers.Length != resources.Length)
            throw new ArgumentException("Every accepted Studio resource kind requires exactly one resolver method.", nameof(resolverMethodIds));
        bool hasResourceParameter = route.Segments.Any(static value => value.Kind == StudioRouteSegmentKind.Parameter &&
            value.Codec == StudioRouteCodec.StudioResourceIdentity);
        if (hasResourceParameter && initialResource is not null ||
            !hasResourceParameter && !resources.IsEmpty && initialResource is null || initialResource is not null &&
            !resources.Contains(initialResource.Kind))
            throw new ArgumentException("Studio landing and resource-parameter pages require exact initial-resource ownership.", nameof(initialResource));
        string[] presentedViewIds = presentation.Sections.SelectMany(static value => value.ViewIds).ToArray();
        if (!StringComparer.Ordinal.Equals(presentation.PageId, pageId) || presentation.PageVersion != version ||
            presentation.NavigationRole != navigationRole || presentedViewIds.Length != ownedViews.Length ||
            presentedViewIds.Except(ownedViews.Select(static value => value.ViewId), StringComparer.Ordinal).Any() ||
            ownedViews.Select(static value => value.ViewId).Distinct(StringComparer.Ordinal).Count() != ownedViews.Length ||
            !ownedViews.Select(static value => value.ObservationMethodId).Order(StringComparer.Ordinal).SequenceEqual(observations) ||
            ownedViews.Any(static value => value.Version < 1 || value.Presentation is null ||
                !StringComparer.Ordinal.Equals(value.ViewId, value.Presentation.ViewId)))
            throw new ArgumentException("Studio page presentation and executable view bindings differ.", nameof(views));
        return new(moduleId, pageId, version, area, navigationRole, route, resources, observations, resolvers, initialResource,
            presentation, ownedViews,
            StudioSha256.FromDigest(registrationChecksum.ToArray()));
    }
}
/// <summary>Projects one command disclosed to the current principal.</summary>
public sealed class StudioVisibleCommand
{
    private StudioVisibleCommand(string moduleId, string commandId, int version, StudioActionClass actionClass,
        ImmutableArray<string> pages, ImmutableArray<StudioResourceKind> resources, StudioSha256 checksum)
    { ModuleId = moduleId; CommandId = commandId; Version = version; ActionClass = actionClass;
      OwningPageIds = pages; AcceptedResources = resources; RegistrationChecksum = checksum; }
    /// <summary>Gets the owning module.</summary>
    public string ModuleId { get; }
    /// <summary>Gets the command identity.</summary>
    public string CommandId { get; }
    /// <summary>Gets the command version.</summary>
    public int Version { get; }
    /// <summary>Gets the minimum review class.</summary>
    public StudioActionClass ActionClass { get; }
    /// <summary>Gets the exact pages on which the command may appear.</summary>
    public ImmutableArray<string> OwningPageIds { get; }
    /// <summary>Gets the exact target resource kinds admitted by the command.</summary>
    public ImmutableArray<StudioResourceKind> AcceptedResources { get; }
    /// <summary>Gets the graph-owned registration checksum.</summary>
    public StudioSha256 RegistrationChecksum { get; }

    /// <summary>Creates one exact principal-filtered command projection.</summary>
    public static StudioVisibleCommand Create(string moduleId, string commandId, int version,
        StudioActionClass actionClass, IEnumerable<string> owningPageIds,
        IEnumerable<StudioResourceKind> acceptedResources, StudioSha256 registrationChecksum)
    {
        StudioContractValidation.Id(moduleId); StudioContractValidation.Id(commandId); StudioContractValidation.Enum(actionClass);
        if (version < 1) throw new ArgumentOutOfRangeException(nameof(version)); ArgumentNullException.ThrowIfNull(registrationChecksum);
        ImmutableArray<string> pages = StudioContractValidation.Ids(owningPageIds, 128, false, nameof(owningPageIds));
        ImmutableArray<StudioResourceKind> resources = StudioContractValidation.Materialize(acceptedResources, 64, false, nameof(acceptedResources));
        if (resources.Any(static value => !Enum.IsDefined(value)) || resources.Distinct().Count() != resources.Length ||
            !resources.SequenceEqual(resources.OrderBy(static value => (byte)value)))
            throw new ArgumentException("Studio command resource kinds are not canonical.", nameof(acceptedResources));
        return new(moduleId, commandId, version, actionClass, pages, resources,
            StudioSha256.FromDigest(registrationChecksum.ToArray()));
    }
}
/// <summary>Projects one resource resolver disclosed to the current principal.</summary>
public sealed record StudioVisibleResourceResolver(string ModuleId, StudioResourceKind Kind,
    string ResolverId, StudioSha256 RegistrationChecksum);
/// <summary>Projects one exact graph-registered link resolver disclosed to the current principal.</summary>
public sealed record StudioVisibleLinkResolver(string ModuleId, StudioResourceKind SourceKind,
    StudioLinkRelation Relation, StudioResourceKind TargetKind, string ResolverId,
    string MethodId, StudioSha256 RegistrationChecksum);
/// <summary>Projects one framework-client contract admitted by bootstrap.</summary>
public sealed record StudioVisibleClient(string ModuleId, string ClientId, int Version,
    StudioFrameworkClientProtocol Protocol, StudioSha256 StaticRuntimeAbiChecksum,
    StudioSha256 GeneratedContractChecksum, StudioSha256 OperationInventoryChecksum,
    string EndpointSurfaceId, StudioFrameworkClientTransportClass TransportClass,
    ImmutableArray<string> OwningPageIds, StudioFrameworkClientLimits Limits,
    ImmutableArray<StudioFrameworkSurfaceOperation> Operations);

/// <summary>Represents one immutable principal-filtered Studio bootstrap snapshot.</summary>
public sealed class StudioBootstrapSnapshot
{
    internal StudioBootstrapSnapshot(string applicationId, StudioMode mode, StudioResponseAuthority authority,
        ImmutableArray<StudioVisibleModule> modules, ImmutableArray<StudioVisiblePage> pages,
        ImmutableArray<StudioVisibleCommand> commands, ImmutableArray<StudioVisibleResourceResolver> resolvers,
        ImmutableArray<StudioVisibleLinkResolver> linkResolvers,
        ImmutableArray<StudioVisibleClient> clients, StudioContractMap contractMap, StudioShellLimits limits,
        DateTimeOffset captured, DateTimeOffset expires, StudioSha256 checksum)
    { ApplicationId = applicationId; Mode = mode; Authority = authority; Modules = modules; Pages = pages; Commands = commands;
      Resolvers = resolvers; LinkResolvers = linkResolvers; Clients = clients; ContractMap = contractMap; Limits = limits;
      CapturedAtUtc = captured; ExpiresAtUtc = expires; SnapshotChecksum = checksum; }
    /// <summary>Gets the Runtime-derived application identity.</summary>
    public string ApplicationId { get; }
    /// <summary>Gets the server-enforced operating mode.</summary>
    public StudioMode Mode { get; }
    /// <summary>Gets the common current authorization envelope.</summary>
    public StudioResponseAuthority Authority { get; }
    /// <summary>Gets authorized modules.</summary>
    public ImmutableArray<StudioVisibleModule> Modules { get; }
    /// <summary>Gets authorized pages.</summary>
    public ImmutableArray<StudioVisiblePage> Pages { get; }
    /// <summary>Gets authorized commands.</summary>
    public ImmutableArray<StudioVisibleCommand> Commands { get; }
    /// <summary>Gets authorized resource resolvers.</summary>
    public ImmutableArray<StudioVisibleResourceResolver> Resolvers { get; }
    /// <summary>Gets authorized registered cross-resource link resolvers.</summary>
    public ImmutableArray<StudioVisibleLinkResolver> LinkResolvers { get; }
    /// <summary>Gets authorized generated clients.</summary>
    public ImmutableArray<StudioVisibleClient> Clients { get; }
    /// <summary>Gets the principal-filtered L41 runtime contract map.</summary>
    public StudioContractMap ContractMap { get; }
    /// <summary>Gets effective shell limits.</summary>
    public StudioShellLimits Limits { get; }
    /// <summary>Gets capture time.</summary>
    public DateTimeOffset CapturedAtUtc { get; }
    /// <summary>Gets snapshot expiry.</summary>
    public DateTimeOffset ExpiresAtUtc { get; }
    /// <summary>Gets the canonical snapshot checksum.</summary>
    public StudioSha256 SnapshotChecksum { get; }

    /// <summary>Creates, validates, deeply owns, and checksums one bootstrap snapshot.</summary>
    public static StudioBootstrapSnapshot Create(string applicationId, StudioMode mode, StudioResponseAuthority authority,
        IEnumerable<StudioVisibleModule> modules, IEnumerable<StudioVisiblePage> pages,
        IEnumerable<StudioVisibleCommand> commands, IEnumerable<StudioVisibleResourceResolver> resolvers,
        IEnumerable<StudioVisibleLinkResolver> linkResolvers,
        IEnumerable<StudioVisibleClient> clients, StudioContractMap contractMap, StudioShellLimits limits,
        DateTimeOffset captured, DateTimeOffset expires)
    {
        StudioContractValidation.Id(applicationId); StudioContractValidation.Enum(mode); ArgumentNullException.ThrowIfNull(authority);
        ArgumentNullException.ThrowIfNull(contractMap); ArgumentNullException.ThrowIfNull(limits);
        if (captured.Offset != TimeSpan.Zero || expires.Offset != TimeSpan.Zero || expires <= captured ||
            expires > authority.AuthorizedThroughUtc) throw new ArgumentException("Studio bootstrap lifetime is invalid.");
        ImmutableArray<StudioVisibleModule> ms = OwnOrdered(modules, limits.MaximumModules, static x => (x.ModuleId, x.Version), nameof(modules), true);
        ImmutableArray<StudioVisiblePage> ps = OwnOrdered(pages, limits.MaximumPages,
            static x => ($"{(byte)x.Area:D3}\0{x.ModuleId}\0{x.PageId}", x.Version), nameof(pages), true);
        ImmutableArray<StudioVisibleCommand> cs = OwnOrdered(commands, limits.MaximumCommands,
            static x => ($"{x.ModuleId}\0{x.CommandId}", x.Version), nameof(commands), true);
        ImmutableArray<StudioVisibleResourceResolver> rs = OwnOrdered(resolvers, limits.MaximumResolvers,
            static x => ($"{x.ModuleId}\0{(byte)x.Kind:D3}\0{x.ResolverId}", 1), nameof(resolvers), true);
        ImmutableArray<StudioVisibleLinkResolver> lrs = OwnOrdered(linkResolvers, limits.MaximumResolvers,
            static x => ($"{x.ModuleId}\0{(byte)x.SourceKind:D3}\0{(byte)x.Relation:D3}\0{(byte)x.TargetKind:D3}\0{x.ResolverId}\0{x.MethodId}", 1), nameof(linkResolvers), true);
        ImmutableArray<StudioVisibleClient> cls = OwnOrdered(clients, limits.MaximumClients,
            static x => ($"{x.ModuleId}\0{x.ClientId}", x.Version), nameof(clients), true);
        if (mode == StudioMode.Inspect && !cs.IsEmpty) throw new ArgumentException("Inspect bootstrap cannot disclose commands.", nameof(commands));
        foreach (StudioVisibleClient client in cls)
        {
            if (client.Protocol == StudioFrameworkClientProtocol.BaseL41DynamicMap && !client.Operations.IsEmpty ||
                client.Protocol == StudioFrameworkClientProtocol.FrameworkGeneratedContractV1 &&
                (client.Operations.IsDefaultOrEmpty || !StudioSha256.FixedTimeEquals(client.OperationInventoryChecksum,
                    StudioFrameworkSurfaceOperation.ComputeInventoryChecksum(client.EndpointSurfaceId, client.Operations))))
                throw new ArgumentException("A visible Studio client operation inventory is invalid.", nameof(clients));
        }
        HashSet<string> moduleIds = ms.Select(static value => value.ModuleId).ToHashSet(StringComparer.Ordinal);
        if (ps.Any(value => !moduleIds.Contains(value.ModuleId)) || cs.Any(value => !moduleIds.Contains(value.ModuleId)) ||
            rs.Any(value => !moduleIds.Contains(value.ModuleId)) || lrs.Any(value => !moduleIds.Contains(value.ModuleId)) || cls.Any(value => !moduleIds.Contains(value.ModuleId)))
            throw new ArgumentException("Studio bootstrap contains a dangling module projection.");
        foreach (StudioVisibleLinkResolver link in lrs)
        {
            StudioContractValidation.Enum(link.SourceKind); StudioContractValidation.Enum(link.Relation); StudioContractValidation.Enum(link.TargetKind);
            StudioContractValidation.Id(link.ResolverId); StudioContractValidation.Id(link.MethodId); ArgumentNullException.ThrowIfNull(link.RegistrationChecksum);
            if (!contractMap.Methods.Any(method => method.Kind == StudioMethodKind.Resolve &&
                StringComparer.Ordinal.Equals(method.RegisteredMethodId, link.MethodId) &&
                StringComparer.Ordinal.Equals(method.OwningModuleId, link.ModuleId) &&
                StringComparer.Ordinal.Equals(method.OwningPageOrCommandId, link.ResolverId)))
                throw new ArgumentException("A Studio link resolver differs from its disclosed contract map.", nameof(linkResolvers));
        }
        foreach (StudioVisiblePage page in ps)
        {
            if (page.ObservationMethodIds.Any(id => !contractMap.Methods.Any(method => method.Kind == StudioMethodKind.Page &&
                    StringComparer.Ordinal.Equals(method.RegisteredMethodId, id) && StringComparer.Ordinal.Equals(method.OwningModuleId, page.ModuleId) &&
                    StringComparer.Ordinal.Equals(method.OwningPageOrCommandId, page.PageId))))
                throw new ArgumentException("A Studio page observation method differs from its disclosed contract map.", nameof(pages));
            foreach (StudioResourceKind acceptedResource in page.AcceptedResources)
            {
                StudioVisibleResourceResolver? resolver = rs.SingleOrDefault(value => value.Kind == acceptedResource &&
                    StringComparer.Ordinal.Equals(value.ModuleId, page.ModuleId));
                if (resolver is null || !page.ResolverMethodIds.Any(methodId => contractMap.Methods.Any(method => method.Kind == StudioMethodKind.Resolve &&
                        StringComparer.Ordinal.Equals(method.RegisteredMethodId, methodId) && StringComparer.Ordinal.Equals(method.OwningModuleId, page.ModuleId) &&
                        StringComparer.Ordinal.Equals(method.OwningPageOrCommandId, resolver.ResolverId))))
                    throw new ArgumentException("A Studio page resource resolver differs from its accepted resource authority.", nameof(pages));
            }
        }
        StudioSha256 checksum = StudioCanonicalEncoding.Hash("base.studio.bootstrap.v1", writer =>
        { writer.String(applicationId); writer.Enum(mode); writer.Checksum(authority.Checksum);
          writer.Count(ms.Length); foreach (StudioVisibleModule x in ms) { writer.String(x.ModuleId); writer.Int32(x.Version); writer.String(x.DisplayNameMessageId); writer.Enum(x.Necessity); writer.Checksum(x.RegistrationChecksum); writer.Checksum(x.FrontendAbiChecksum); writer.Checksum(x.AssetGraphChecksum); }
          writer.Count(ps.Length); foreach (StudioVisiblePage x in ps) { writer.String(x.ModuleId); writer.String(x.PageId); writer.Int32(x.Version); writer.Enum(x.Area); writer.Enum(x.NavigationRole); writer.Checksum(x.Route.Checksum);
            writer.Count(x.AcceptedResources.Length); foreach (StudioResourceKind resource in x.AcceptedResources) writer.Enum(resource);
            writer.Count(x.ObservationMethodIds.Length); foreach (string method in x.ObservationMethodIds) writer.String(method);
            writer.Count(x.ResolverMethodIds.Length); foreach (string method in x.ResolverMethodIds) writer.String(method);
            writer.OptionalChecksum(x.InitialResource?.AuthorityChecksum);
            writer.Checksum(x.Presentation.Checksum); writer.Count(x.Views.Length);
            foreach (StudioVisibleView view in x.Views) { writer.String(view.ViewId); writer.Int32(view.Version);
              writer.String(view.ObservationMethodId); writer.Enum(view.ItemKind); writer.String(view.ItemNodeId);
              writer.Checksum(view.ItemNodeChecksum); writer.Checksum(view.Presentation.Checksum); writer.Checksum(view.RegistrationChecksum); }
            writer.Checksum(x.RegistrationChecksum); }
          writer.Count(cs.Length); foreach (StudioVisibleCommand x in cs) { writer.String(x.ModuleId); writer.String(x.CommandId); writer.Int32(x.Version); writer.Enum(x.ActionClass);
            writer.Count(x.OwningPageIds.Length); foreach (string page in x.OwningPageIds) writer.String(page);
            writer.Count(x.AcceptedResources.Length); foreach (StudioResourceKind resource in x.AcceptedResources) writer.Enum(resource);
            writer.Checksum(x.RegistrationChecksum); }
          writer.Count(rs.Length); foreach (StudioVisibleResourceResolver x in rs) { writer.String(x.ModuleId); writer.Enum(x.Kind); writer.String(x.ResolverId); writer.Checksum(x.RegistrationChecksum); }
          writer.Count(lrs.Length); foreach (StudioVisibleLinkResolver x in lrs) { writer.String(x.ModuleId); writer.Enum(x.SourceKind); writer.Enum(x.Relation); writer.Enum(x.TargetKind); writer.String(x.ResolverId); writer.String(x.MethodId); writer.Checksum(x.RegistrationChecksum); }
          writer.Count(cls.Length); foreach (StudioVisibleClient x in cls) { writer.String(x.ModuleId); writer.String(x.ClientId); writer.Int32(x.Version); writer.Enum(x.Protocol);
            writer.Checksum(x.StaticRuntimeAbiChecksum); writer.Checksum(x.GeneratedContractChecksum); writer.Checksum(x.OperationInventoryChecksum);
            writer.String(x.EndpointSurfaceId); writer.Enum(x.TransportClass); writer.Count(x.OwningPageIds.Length);
            foreach (string page in x.OwningPageIds) writer.String(page); writer.Checksum(x.Limits.Checksum); writer.Count(x.Operations.Length);
            foreach (StudioFrameworkSurfaceOperation operation in x.Operations)
            { writer.String(operation.OperationId); writer.Enum(operation.Method); writer.String(operation.RelativePathTemplate); writer.Enum(operation.Purpose);
              writer.String(operation.RequiredCapability); writer.Int64(operation.MaximumRequestBytes); writer.Int64(operation.MaximumResponseBytes);
              writer.Int64(checked((long)operation.Deadline.TotalMilliseconds)); } }
          writer.Checksum(contractMap.Checksum); writer.Checksum(limits.Checksum); writer.String(StudioResponseAuthority.CanonicalUtc(captured));
          writer.String(StudioResponseAuthority.CanonicalUtc(expires)); });
        return new(applicationId, mode, authority, ms, ps, cs, rs, lrs, cls, contractMap, limits, captured, expires, checksum);
    }

    private static ImmutableArray<T> OwnOrdered<T>(IEnumerable<T> source, int maximum, Func<T, (string Id, int Version)> key,
        string parameter, bool allowEmpty = false) where T : class
    {
        ImmutableArray<T> result = StudioContractValidation.Materialize(source, maximum, allowEmpty, parameter);
        var keys = result.Select(key).ToArray();
        if (!keys.SequenceEqual(keys.OrderBy(static x => x.Id, StringComparer.Ordinal).ThenBy(static x => x.Version)) || keys.Distinct().Count() != keys.Length)
            throw new ArgumentException("Studio bootstrap projection is not canonical.", parameter);
        return result;
    }

}
