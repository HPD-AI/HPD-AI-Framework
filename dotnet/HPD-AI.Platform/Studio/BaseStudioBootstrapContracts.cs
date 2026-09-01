using System.Collections.Immutable;

namespace HPD.AI.Platform.Studio;

/// <summary>Identifies the server-enforced Studio operating mode.</summary>
public enum BaseStudioMode : byte { Inspect = 1, Operate }
/// <summary>Identifies one browser capability admitted by bootstrap.</summary>
public enum BaseStudioBrowserCapability : byte { History = 1, ModuleScripts, WebSocket, ResizeObserver, AbortSignal }

/// <summary>Describes the exact static browser build requesting bootstrap.</summary>
public sealed class BaseStudioBootstrapRequest
{
    private BaseStudioBootstrapRequest(BaseStudioSha256 shell, BaseStudioSha256 assets, BaseStudioSha256 client,
        string locale, ImmutableArray<BaseStudioBrowserCapability> capabilities)
    { ShellContractChecksum = shell; EditionAssetGraphChecksum = assets; RuntimeClientChecksum = client; Locale = locale; ClientCapabilities = capabilities; }
    /// <summary>Gets the static shell ABI checksum.</summary>
    public BaseStudioSha256 ShellContractChecksum { get; }
    /// <summary>Gets the authorization-neutral edition asset checksum.</summary>
    public BaseStudioSha256 EditionAssetGraphChecksum { get; }
    /// <summary>Gets the runtime L41 interpreter checksum.</summary>
    public BaseStudioSha256 RuntimeClientChecksum { get; }
    /// <summary>Gets the bounded BCP-47 locale.</summary>
    public string Locale { get; }
    /// <summary>Gets browser capabilities in discriminator order.</summary>
    public ImmutableArray<BaseStudioBrowserCapability> ClientCapabilities { get; }

    /// <summary>Creates a deeply owned bootstrap request.</summary>
    public static BaseStudioBootstrapRequest Create(BaseStudioSha256 shell, BaseStudioSha256 assets,
        BaseStudioSha256 client, string locale, IEnumerable<BaseStudioBrowserCapability> capabilities)
    {
        ArgumentNullException.ThrowIfNull(shell); ArgumentNullException.ThrowIfNull(assets); ArgumentNullException.ThrowIfNull(client);
        ArgumentException.ThrowIfNullOrWhiteSpace(locale);
        if (locale.Length > 35 || !locale.All(static value => char.IsAsciiLetterOrDigit(value) || value == '-'))
            throw new ArgumentException("Studio locale is invalid.", nameof(locale));
        ImmutableArray<BaseStudioBrowserCapability> owned = StudioContractValidation.Materialize(capabilities, 5, false, nameof(capabilities));
        if (owned.Any(static value => !Enum.IsDefined(value)) || owned.Distinct().Count() != owned.Length ||
            !owned.SequenceEqual(owned.OrderBy(static value => (byte)value)))
            throw new ArgumentException("Studio browser capabilities are not canonical.", nameof(capabilities));
        return new(BaseStudioSha256.FromBytes(shell.ToArray()), BaseStudioSha256.FromBytes(assets.ToArray()),
            BaseStudioSha256.FromBytes(client.ToArray()), new string(locale.AsSpan()), owned);
    }
}

/// <summary>Projects one module disclosed to the current principal.</summary>
public sealed record BaseStudioVisibleModule(string ModuleId, int Version, string DisplayNameMessageId,
    BaseStudioModuleNecessity Necessity, BaseStudioSha256 RegistrationChecksum,
    BaseStudioSha256 FrontendAbiChecksum, BaseStudioSha256 AssetGraphChecksum);
/// <summary>Binds one authorized view's frozen presentation to its exact observation method.</summary>
public sealed record BaseStudioVisibleView(string ViewId, int Version, string ObservationMethodId,
    BaseStudioResourceKind ItemKind, string ItemNodeId, BaseStudioSha256 ItemNodeChecksum,
    BaseStudioViewPresentationRegistration Presentation, BaseStudioSha256 RegistrationChecksum);
/// <summary>Projects one page disclosed to the current principal.</summary>
public sealed class BaseStudioVisiblePage
{
    private BaseStudioVisiblePage(string moduleId, string pageId, int version, BaseStudioArea area,
        BaseStudioNavigationRole navigationRole, BaseStudioRouteTemplate route,
        ImmutableArray<BaseStudioResourceKind> acceptedResources, ImmutableArray<string> observationMethodIds,
        ImmutableArray<string> resolverMethodIds, BaseStudioResourceIdentity? initialResource,
        BaseStudioPagePresentationRegistration presentation, ImmutableArray<BaseStudioVisibleView> views,
        BaseStudioSha256 registrationChecksum)
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
    public BaseStudioArea Area { get; }
    /// <summary>Gets the navigation role.</summary>
    public BaseStudioNavigationRole NavigationRole { get; }
    /// <summary>Gets the typed route template.</summary>
    public BaseStudioRouteTemplate Route { get; }
    /// <summary>Gets the exact outward resource kinds accepted by this page.</summary>
    public ImmutableArray<BaseStudioResourceKind> AcceptedResources { get; }
    /// <summary>Gets the registered page-observation methods in canonical identity order.</summary>
    public ImmutableArray<string> ObservationMethodIds { get; }
    /// <summary>Gets the registered resource-resolver methods in canonical identity order.</summary>
    public ImmutableArray<string> ResolverMethodIds { get; }
    /// <summary>Gets the server-issued initial resource for a landing page, or <see langword="null"/> for resource-parameter pages.</summary>
    public BaseStudioResourceIdentity? InitialResource { get; }
    /// <summary>Gets the exact frozen page presentation.</summary>
    public BaseStudioPagePresentationRegistration Presentation { get; }
    /// <summary>Gets page-owned view presentations bound to exact observation methods.</summary>
    public ImmutableArray<BaseStudioVisibleView> Views { get; }
    /// <summary>Gets the graph-owned page registration checksum.</summary>
    public BaseStudioSha256 RegistrationChecksum { get; }

    /// <summary>Creates a deeply owned executable page projection.</summary>
    public static BaseStudioVisiblePage Create(string moduleId, string pageId, int version, BaseStudioArea area,
        BaseStudioNavigationRole navigationRole, BaseStudioRouteTemplate route,
        IEnumerable<BaseStudioResourceKind> acceptedResources, IEnumerable<string> observationMethodIds,
        IEnumerable<string> resolverMethodIds, BaseStudioResourceIdentity? initialResource,
        BaseStudioPagePresentationRegistration presentation, IEnumerable<BaseStudioVisibleView> views,
        BaseStudioSha256 registrationChecksum)
    {
        StudioContractValidation.Id(moduleId); StudioContractValidation.Id(pageId); StudioContractValidation.Enum(area);
        StudioContractValidation.Enum(navigationRole); ArgumentNullException.ThrowIfNull(route);
        ArgumentNullException.ThrowIfNull(registrationChecksum); ArgumentNullException.ThrowIfNull(presentation);
        if (version < 1) throw new ArgumentOutOfRangeException(nameof(version));
        ImmutableArray<BaseStudioResourceKind> resources = StudioContractValidation.Materialize(acceptedResources, 64, true, nameof(acceptedResources));
        if (resources.Any(static value => !Enum.IsDefined(value)) || resources.Distinct().Count() != resources.Length ||
            !resources.SequenceEqual(resources.OrderBy(static value => (byte)value)))
            throw new ArgumentException("Studio page resources are not canonical.", nameof(acceptedResources));
        ImmutableArray<string> observations = StudioContractValidation.Ids(observationMethodIds, 256, true, nameof(observationMethodIds));
        ImmutableArray<string> resolvers = StudioContractValidation.Ids(resolverMethodIds, 64, resources.IsEmpty, nameof(resolverMethodIds));
        ImmutableArray<BaseStudioVisibleView> ownedViews = StudioContractValidation.Materialize(views, 256, true, nameof(views));
        if (resolvers.Length != resources.Length)
            throw new ArgumentException("Every accepted Studio resource kind requires exactly one resolver method.", nameof(resolverMethodIds));
        bool hasResourceParameter = route.Segments.Any(static value => value.Kind == BaseStudioRouteSegmentKind.Parameter &&
            value.Codec == BaseStudioRouteCodec.StudioResourceIdentity);
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
            BaseStudioSha256.FromDigest(registrationChecksum.ToArray()));
    }
}
/// <summary>Projects one command disclosed to the current principal.</summary>
public sealed class BaseStudioVisibleCommand
{
    private BaseStudioVisibleCommand(string moduleId, string commandId, int version, BaseStudioActionClass actionClass,
        ImmutableArray<string> pages, ImmutableArray<BaseStudioResourceKind> resources, BaseStudioSha256 checksum)
    { ModuleId = moduleId; CommandId = commandId; Version = version; ActionClass = actionClass;
      OwningPageIds = pages; AcceptedResources = resources; RegistrationChecksum = checksum; }
    /// <summary>Gets the owning module.</summary>
    public string ModuleId { get; }
    /// <summary>Gets the command identity.</summary>
    public string CommandId { get; }
    /// <summary>Gets the command version.</summary>
    public int Version { get; }
    /// <summary>Gets the minimum review class.</summary>
    public BaseStudioActionClass ActionClass { get; }
    /// <summary>Gets the exact pages on which the command may appear.</summary>
    public ImmutableArray<string> OwningPageIds { get; }
    /// <summary>Gets the exact target resource kinds admitted by the command.</summary>
    public ImmutableArray<BaseStudioResourceKind> AcceptedResources { get; }
    /// <summary>Gets the graph-owned registration checksum.</summary>
    public BaseStudioSha256 RegistrationChecksum { get; }

    /// <summary>Creates one exact principal-filtered command projection.</summary>
    public static BaseStudioVisibleCommand Create(string moduleId, string commandId, int version,
        BaseStudioActionClass actionClass, IEnumerable<string> owningPageIds,
        IEnumerable<BaseStudioResourceKind> acceptedResources, BaseStudioSha256 registrationChecksum)
    {
        StudioContractValidation.Id(moduleId); StudioContractValidation.Id(commandId); StudioContractValidation.Enum(actionClass);
        if (version < 1) throw new ArgumentOutOfRangeException(nameof(version)); ArgumentNullException.ThrowIfNull(registrationChecksum);
        ImmutableArray<string> pages = StudioContractValidation.Ids(owningPageIds, 128, false, nameof(owningPageIds));
        ImmutableArray<BaseStudioResourceKind> resources = StudioContractValidation.Materialize(acceptedResources, 64, false, nameof(acceptedResources));
        if (resources.Any(static value => !Enum.IsDefined(value)) || resources.Distinct().Count() != resources.Length ||
            !resources.SequenceEqual(resources.OrderBy(static value => (byte)value)))
            throw new ArgumentException("Studio command resource kinds are not canonical.", nameof(acceptedResources));
        return new(moduleId, commandId, version, actionClass, pages, resources,
            BaseStudioSha256.FromDigest(registrationChecksum.ToArray()));
    }
}
/// <summary>Projects one resource resolver disclosed to the current principal.</summary>
public sealed record BaseStudioVisibleResourceResolver(string ModuleId, BaseStudioResourceKind Kind,
    string ResolverId, BaseStudioSha256 RegistrationChecksum);
/// <summary>Projects one exact graph-registered link resolver disclosed to the current principal.</summary>
public sealed record BaseStudioVisibleLinkResolver(string ModuleId, BaseStudioResourceKind SourceKind,
    BaseStudioLinkRelation Relation, BaseStudioResourceKind TargetKind, string ResolverId,
    string MethodId, BaseStudioSha256 RegistrationChecksum);
/// <summary>Projects one framework-client contract admitted by bootstrap.</summary>
public sealed record BaseStudioVisibleClient(string ModuleId, string ClientId, int Version,
    BaseStudioFrameworkClientProtocol Protocol, BaseStudioSha256 StaticRuntimeAbiChecksum,
    BaseStudioSha256 GeneratedContractChecksum, BaseStudioSha256 OperationInventoryChecksum,
    string EndpointSurfaceId, BaseStudioFrameworkClientTransportClass TransportClass,
    ImmutableArray<string> OwningPageIds, BaseStudioFrameworkClientLimits Limits,
    ImmutableArray<BaseStudioFrameworkSurfaceOperation> Operations);

/// <summary>Represents one immutable principal-filtered Studio bootstrap snapshot.</summary>
public sealed class BaseStudioBootstrapSnapshot
{
    internal BaseStudioBootstrapSnapshot(string applicationId, BaseStudioMode mode, BaseStudioResponseAuthority authority,
        ImmutableArray<BaseStudioVisibleModule> modules, ImmutableArray<BaseStudioVisiblePage> pages,
        ImmutableArray<BaseStudioVisibleCommand> commands, ImmutableArray<BaseStudioVisibleResourceResolver> resolvers,
        ImmutableArray<BaseStudioVisibleLinkResolver> linkResolvers,
        ImmutableArray<BaseStudioVisibleClient> clients, BaseStudioContractMap contractMap, BaseStudioShellLimits limits,
        DateTimeOffset captured, DateTimeOffset expires, BaseStudioSha256 checksum)
    { ApplicationId = applicationId; Mode = mode; Authority = authority; Modules = modules; Pages = pages; Commands = commands;
      Resolvers = resolvers; LinkResolvers = linkResolvers; Clients = clients; ContractMap = contractMap; Limits = limits;
      CapturedAtUtc = captured; ExpiresAtUtc = expires; SnapshotChecksum = checksum; }
    /// <summary>Gets the Runtime-derived application identity.</summary>
    public string ApplicationId { get; }
    /// <summary>Gets the server-enforced operating mode.</summary>
    public BaseStudioMode Mode { get; }
    /// <summary>Gets the common current authorization envelope.</summary>
    public BaseStudioResponseAuthority Authority { get; }
    /// <summary>Gets authorized modules.</summary>
    public ImmutableArray<BaseStudioVisibleModule> Modules { get; }
    /// <summary>Gets authorized pages.</summary>
    public ImmutableArray<BaseStudioVisiblePage> Pages { get; }
    /// <summary>Gets authorized commands.</summary>
    public ImmutableArray<BaseStudioVisibleCommand> Commands { get; }
    /// <summary>Gets authorized resource resolvers.</summary>
    public ImmutableArray<BaseStudioVisibleResourceResolver> Resolvers { get; }
    /// <summary>Gets authorized registered cross-resource link resolvers.</summary>
    public ImmutableArray<BaseStudioVisibleLinkResolver> LinkResolvers { get; }
    /// <summary>Gets authorized generated clients.</summary>
    public ImmutableArray<BaseStudioVisibleClient> Clients { get; }
    /// <summary>Gets the principal-filtered L41 runtime contract map.</summary>
    public BaseStudioContractMap ContractMap { get; }
    /// <summary>Gets effective shell limits.</summary>
    public BaseStudioShellLimits Limits { get; }
    /// <summary>Gets capture time.</summary>
    public DateTimeOffset CapturedAtUtc { get; }
    /// <summary>Gets snapshot expiry.</summary>
    public DateTimeOffset ExpiresAtUtc { get; }
    /// <summary>Gets the canonical snapshot checksum.</summary>
    public BaseStudioSha256 SnapshotChecksum { get; }

    /// <summary>Creates, validates, deeply owns, and checksums one bootstrap snapshot.</summary>
    public static BaseStudioBootstrapSnapshot Create(string applicationId, BaseStudioMode mode, BaseStudioResponseAuthority authority,
        IEnumerable<BaseStudioVisibleModule> modules, IEnumerable<BaseStudioVisiblePage> pages,
        IEnumerable<BaseStudioVisibleCommand> commands, IEnumerable<BaseStudioVisibleResourceResolver> resolvers,
        IEnumerable<BaseStudioVisibleLinkResolver> linkResolvers,
        IEnumerable<BaseStudioVisibleClient> clients, BaseStudioContractMap contractMap, BaseStudioShellLimits limits,
        DateTimeOffset captured, DateTimeOffset expires)
    {
        StudioContractValidation.Id(applicationId); StudioContractValidation.Enum(mode); ArgumentNullException.ThrowIfNull(authority);
        ArgumentNullException.ThrowIfNull(contractMap); ArgumentNullException.ThrowIfNull(limits);
        if (captured.Offset != TimeSpan.Zero || expires.Offset != TimeSpan.Zero || expires <= captured ||
            expires > authority.AuthorizedThroughUtc) throw new ArgumentException("Studio bootstrap lifetime is invalid.");
        ImmutableArray<BaseStudioVisibleModule> ms = OwnOrdered(modules, limits.MaximumModules, static x => (x.ModuleId, x.Version), nameof(modules), true);
        ImmutableArray<BaseStudioVisiblePage> ps = OwnOrdered(pages, limits.MaximumPages,
            static x => ($"{(byte)x.Area:D3}\0{x.ModuleId}\0{x.PageId}", x.Version), nameof(pages), true);
        ImmutableArray<BaseStudioVisibleCommand> cs = OwnOrdered(commands, limits.MaximumCommands,
            static x => ($"{x.ModuleId}\0{x.CommandId}", x.Version), nameof(commands), true);
        ImmutableArray<BaseStudioVisibleResourceResolver> rs = OwnOrdered(resolvers, limits.MaximumResolvers,
            static x => ($"{x.ModuleId}\0{(byte)x.Kind:D3}\0{x.ResolverId}", 1), nameof(resolvers), true);
        ImmutableArray<BaseStudioVisibleLinkResolver> lrs = OwnOrdered(linkResolvers, limits.MaximumResolvers,
            static x => ($"{x.ModuleId}\0{(byte)x.SourceKind:D3}\0{(byte)x.Relation:D3}\0{(byte)x.TargetKind:D3}\0{x.ResolverId}\0{x.MethodId}", 1), nameof(linkResolvers), true);
        ImmutableArray<BaseStudioVisibleClient> cls = OwnOrdered(clients, limits.MaximumClients,
            static x => ($"{x.ModuleId}\0{x.ClientId}", x.Version), nameof(clients), true);
        if (mode == BaseStudioMode.Inspect && !cs.IsEmpty) throw new ArgumentException("Inspect bootstrap cannot disclose commands.", nameof(commands));
        foreach (BaseStudioVisibleClient client in cls)
        {
            if (client.Protocol == BaseStudioFrameworkClientProtocol.BaseL41DynamicMap && !client.Operations.IsEmpty ||
                client.Protocol == BaseStudioFrameworkClientProtocol.FrameworkGeneratedContractV1 &&
                (client.Operations.IsDefaultOrEmpty || !BaseStudioSha256.FixedTimeEquals(client.OperationInventoryChecksum,
                    BaseStudioFrameworkSurfaceOperation.ComputeInventoryChecksum(client.EndpointSurfaceId, client.Operations))))
                throw new ArgumentException("A visible Studio client operation inventory is invalid.", nameof(clients));
        }
        HashSet<string> moduleIds = ms.Select(static value => value.ModuleId).ToHashSet(StringComparer.Ordinal);
        if (ps.Any(value => !moduleIds.Contains(value.ModuleId)) || cs.Any(value => !moduleIds.Contains(value.ModuleId)) ||
            rs.Any(value => !moduleIds.Contains(value.ModuleId)) || lrs.Any(value => !moduleIds.Contains(value.ModuleId)) || cls.Any(value => !moduleIds.Contains(value.ModuleId)))
            throw new ArgumentException("Studio bootstrap contains a dangling module projection.");
        foreach (BaseStudioVisibleLinkResolver link in lrs)
        {
            StudioContractValidation.Enum(link.SourceKind); StudioContractValidation.Enum(link.Relation); StudioContractValidation.Enum(link.TargetKind);
            StudioContractValidation.Id(link.ResolverId); StudioContractValidation.Id(link.MethodId); ArgumentNullException.ThrowIfNull(link.RegistrationChecksum);
            if (!contractMap.Methods.Any(method => method.Kind == BaseStudioMethodKind.Resolve &&
                StringComparer.Ordinal.Equals(method.RegisteredMethodId, link.MethodId) &&
                StringComparer.Ordinal.Equals(method.OwningModuleId, link.ModuleId) &&
                StringComparer.Ordinal.Equals(method.OwningPageOrCommandId, link.ResolverId)))
                throw new ArgumentException("A Studio link resolver differs from its disclosed contract map.", nameof(linkResolvers));
        }
        foreach (BaseStudioVisiblePage page in ps)
        {
            if (page.ObservationMethodIds.Any(id => !contractMap.Methods.Any(method => method.Kind == BaseStudioMethodKind.Page &&
                    StringComparer.Ordinal.Equals(method.RegisteredMethodId, id) && StringComparer.Ordinal.Equals(method.OwningModuleId, page.ModuleId) &&
                    StringComparer.Ordinal.Equals(method.OwningPageOrCommandId, page.PageId))))
                throw new ArgumentException("A Studio page observation method differs from its disclosed contract map.", nameof(pages));
            foreach (BaseStudioResourceKind acceptedResource in page.AcceptedResources)
            {
                BaseStudioVisibleResourceResolver? resolver = rs.SingleOrDefault(value => value.Kind == acceptedResource &&
                    StringComparer.Ordinal.Equals(value.ModuleId, page.ModuleId));
                if (resolver is null || !page.ResolverMethodIds.Any(methodId => contractMap.Methods.Any(method => method.Kind == BaseStudioMethodKind.Resolve &&
                        StringComparer.Ordinal.Equals(method.RegisteredMethodId, methodId) && StringComparer.Ordinal.Equals(method.OwningModuleId, page.ModuleId) &&
                        StringComparer.Ordinal.Equals(method.OwningPageOrCommandId, resolver.ResolverId))))
                    throw new ArgumentException("A Studio page resource resolver differs from its accepted resource authority.", nameof(pages));
            }
        }
        BaseStudioSha256 checksum = StudioCanonicalEncoding.Hash("base.studio.bootstrap.v1", writer =>
        { writer.String(applicationId); writer.Enum(mode); writer.Checksum(authority.Checksum);
          writer.Count(ms.Length); foreach (BaseStudioVisibleModule x in ms) { writer.String(x.ModuleId); writer.Int32(x.Version); writer.String(x.DisplayNameMessageId); writer.Enum(x.Necessity); writer.Checksum(x.RegistrationChecksum); writer.Checksum(x.FrontendAbiChecksum); writer.Checksum(x.AssetGraphChecksum); }
          writer.Count(ps.Length); foreach (BaseStudioVisiblePage x in ps) { writer.String(x.ModuleId); writer.String(x.PageId); writer.Int32(x.Version); writer.Enum(x.Area); writer.Enum(x.NavigationRole); writer.Checksum(x.Route.Checksum);
            writer.Count(x.AcceptedResources.Length); foreach (BaseStudioResourceKind resource in x.AcceptedResources) writer.Enum(resource);
            writer.Count(x.ObservationMethodIds.Length); foreach (string method in x.ObservationMethodIds) writer.String(method);
            writer.Count(x.ResolverMethodIds.Length); foreach (string method in x.ResolverMethodIds) writer.String(method);
            writer.OptionalChecksum(x.InitialResource?.AuthorityChecksum);
            writer.Checksum(x.Presentation.Checksum); writer.Count(x.Views.Length);
            foreach (BaseStudioVisibleView view in x.Views) { writer.String(view.ViewId); writer.Int32(view.Version);
              writer.String(view.ObservationMethodId); writer.Enum(view.ItemKind); writer.String(view.ItemNodeId);
              writer.Checksum(view.ItemNodeChecksum); writer.Checksum(view.Presentation.Checksum); writer.Checksum(view.RegistrationChecksum); }
            writer.Checksum(x.RegistrationChecksum); }
          writer.Count(cs.Length); foreach (BaseStudioVisibleCommand x in cs) { writer.String(x.ModuleId); writer.String(x.CommandId); writer.Int32(x.Version); writer.Enum(x.ActionClass);
            writer.Count(x.OwningPageIds.Length); foreach (string page in x.OwningPageIds) writer.String(page);
            writer.Count(x.AcceptedResources.Length); foreach (BaseStudioResourceKind resource in x.AcceptedResources) writer.Enum(resource);
            writer.Checksum(x.RegistrationChecksum); }
          writer.Count(rs.Length); foreach (BaseStudioVisibleResourceResolver x in rs) { writer.String(x.ModuleId); writer.Enum(x.Kind); writer.String(x.ResolverId); writer.Checksum(x.RegistrationChecksum); }
          writer.Count(lrs.Length); foreach (BaseStudioVisibleLinkResolver x in lrs) { writer.String(x.ModuleId); writer.Enum(x.SourceKind); writer.Enum(x.Relation); writer.Enum(x.TargetKind); writer.String(x.ResolverId); writer.String(x.MethodId); writer.Checksum(x.RegistrationChecksum); }
          writer.Count(cls.Length); foreach (BaseStudioVisibleClient x in cls) { writer.String(x.ModuleId); writer.String(x.ClientId); writer.Int32(x.Version); writer.Enum(x.Protocol);
            writer.Checksum(x.StaticRuntimeAbiChecksum); writer.Checksum(x.GeneratedContractChecksum); writer.Checksum(x.OperationInventoryChecksum);
            writer.String(x.EndpointSurfaceId); writer.Enum(x.TransportClass); writer.Count(x.OwningPageIds.Length);
            foreach (string page in x.OwningPageIds) writer.String(page); writer.Checksum(x.Limits.Checksum); writer.Count(x.Operations.Length);
            foreach (BaseStudioFrameworkSurfaceOperation operation in x.Operations)
            { writer.String(operation.OperationId); writer.Enum(operation.Method); writer.String(operation.RelativePathTemplate); writer.Enum(operation.Purpose);
              writer.String(operation.RequiredCapability); writer.Int64(operation.MaximumRequestBytes); writer.Int64(operation.MaximumResponseBytes);
              writer.Int64(checked((long)operation.Deadline.TotalMilliseconds)); } }
          writer.Checksum(contractMap.Checksum); writer.Checksum(limits.Checksum); writer.String(BaseStudioResponseAuthority.CanonicalUtc(captured));
          writer.String(BaseStudioResponseAuthority.CanonicalUtc(expires)); });
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
