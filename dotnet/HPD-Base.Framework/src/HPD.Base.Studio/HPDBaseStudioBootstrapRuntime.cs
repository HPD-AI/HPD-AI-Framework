using HPD.AI.Platform.Studio;
using System.Collections.Immutable;

namespace HPD.Base.Studio;

internal sealed class HPDBaseStudioBootstrapRuntime : IStudioBootstrapRuntime
{
    private readonly HPDBaseStudioAuthorization _authorization;
    private readonly StudioRuntimeCatalog _runtime;
    private readonly HPDBaseStudioAuthoritySnapshot _baseAuthority;
    private readonly IStudioDynamicStoreAuthoritySource _storeAuthority;
    private readonly ImmutableArray<IStudioFrameworkEndpointSurface> _frameworkSurfaces;
    private readonly StudioLateWorkRegistry _lateWork;
    private readonly TimeProvider _timeProvider;
    private readonly HPDBaseStudioOptions _options;
    private readonly StudioAuthenticationProvider _authentication;

    public HPDBaseStudioBootstrapRuntime(HPDBaseStudioAuthorization authorization, StudioRuntimeCatalog runtime,
        HPDBaseStudioAuthoritySnapshot baseAuthority, IRecordStore recordStore,
        IRecordMutationStore mutationStore, IAtomicRecordStore atomicStore,
        IEnumerable<IStudioFrameworkEndpointSurface> frameworkSurfaces,
        StudioLateWorkRegistry lateWork, TimeProvider timeProvider, HPDBaseStudioOptions options,
        StudioAuthenticationProvider authentication)
    { _authorization = authorization; _runtime = runtime; _baseAuthority = baseAuthority;
      if (!ReferenceEquals(recordStore, atomicStore) || !ReferenceEquals(mutationStore, atomicStore))
          throw new InvalidOperationException("base.studio.installedStoreAuthoritySubstituted");
      _storeAuthority = atomicStore as IStudioDynamicStoreAuthoritySource ??
          throw new InvalidOperationException("base.studio.installedStoreAuthorityUnavailable");
      _frameworkSurfaces = frameworkSurfaces.ToImmutableArray();
      _lateWork = lateWork; _timeProvider = timeProvider; _options = options; _authentication = authentication; }

    public async ValueTask<StudioBootstrapSnapshot?> CreateAsync(StudioBootstrapInvocation invocation,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(invocation);
        StudioModuleRegistration baseModule = invocation.ApplicationGraph.Modules.Single(static value => value.Identity.ModuleId == "base");
        StudioModuleRuntimeContribution contribution = _runtime.Contributions.Single(static value => value.ModuleId == "base");
        StudioFrameworkClientRegistration baseClient = baseModule.Clients.Single(static value => value.ClientId == "base.control-plane");
        if (!StudioSha256.FixedTimeEquals(invocation.Request.RuntimeClientChecksum, baseClient.StaticRuntimeAbiChecksum) ||
            await _authorization.ResolvePrincipalAsync(invocation, cancellationToken).ConfigureAwait(false) is null)
            return null;
        if (!await AdmittedAsync(invocation, baseModule.Grants, cancellationToken).ConfigureAwait(false)) return null;

        var visiblePages = new List<StudioVisiblePage>();
        var visibleResolvers = new List<StudioVisibleResourceResolver>();
        var disclosedMethodIds = new HashSet<string>(StringComparer.Ordinal);
        var disclosedOwners = new HashSet<(string ModuleId, string OwnerId)>();
        var supportedBasePages = new List<StudioPageRegistration>();
        foreach (StudioPageRegistration page in baseModule.Pages)
        {
            string[] viewIds = page.Presentation.Sections.SelectMany(static value => value.ViewIds).ToArray();
            StudioMethodBinding[] pageMethods = contribution.Methods.Where(value => value.Kind == StudioMethodKind.Page &&
                StringComparer.Ordinal.Equals(value.OwningPageOrCommandId, page.PageId)).ToArray();
            if (pageMethods.Length != viewIds.Length || viewIds.Any(viewId =>
                    pageMethods.Count(method => method.RegisteredMethodId.EndsWith(viewId, StringComparison.Ordinal)) != 1)) continue;
            var pageResources = new List<(StudioResourceRegistration Registration, StudioMethodBinding Method)>();
            bool complete = true;
            foreach (StudioResourceKind kind in page.AcceptedResources)
            {
                StudioResourceRegistration registration = baseModule.Resources.Single(value => value.Kind == kind);
                StudioMethodBinding? method = contribution.Methods.SingleOrDefault(value => value.Kind == StudioMethodKind.Resolve &&
                    StringComparer.Ordinal.Equals(value.OwningPageOrCommandId, registration.ResolverId));
                if (method is null || !await AdmittedAsync(invocation, registration.Grants, cancellationToken).ConfigureAwait(false))
                { complete = false; break; }
                pageResources.Add((registration, method));
            }
            if (!complete || !await AdmittedAsync(invocation, page.Grants, cancellationToken).ConfigureAwait(false)) continue;
            StudioResourceIdentity? initial = page.Route.Segments.Any(static value => value.Kind == StudioRouteSegmentKind.Parameter)
                ? null : page.AcceptedResources.Contains(StudioResourceKind.Application)
                    ? new StudioApplicationResource(invocation.ApplicationGraph.ApplicationId) : null;
            if (initial is null && !page.Route.Segments.Any(static value => value.Kind == StudioRouteSegmentKind.Parameter) && !page.AcceptedResources.IsEmpty)
                continue;
            string[] observations = pageMethods.Select(static value => value.RegisteredMethodId).Order(StringComparer.Ordinal).ToArray();
            string[] resolverMethods = pageResources.Select(static value => value.Method.RegisteredMethodId).Order(StringComparer.Ordinal).ToArray();
            StudioVisibleView[] views = viewIds.Select(viewId =>
            {
                StudioViewRegistration view = baseModule.Views.Single(value => StringComparer.Ordinal.Equals(value.ViewId, viewId));
                StudioMethodBinding method = pageMethods.Single(value => value.RegisteredMethodId.EndsWith(viewId, StringComparison.Ordinal));
                return new StudioVisibleView(view.ViewId, view.Version, method.RegisteredMethodId, view.ItemKind, view.ItemNodeId,
                    StudioSha256.FromDigest(view.ItemNodeChecksum.ToArray()), view.Presentation,
                    StudioSha256.FromDigest(view.Checksum.ToArray()));
            }).OrderBy(static value => value.ViewId, StringComparer.Ordinal).ToArray();
            if (_options.ShowBaseModule)
                visiblePages.Add(StudioVisiblePage.Create("base", page.PageId, page.Version, page.Area,
                    page.Presentation.NavigationRole, page.Route, page.AcceptedResources, observations, resolverMethods,
                    initial, page.Presentation, views, StudioSha256.FromDigest(page.Checksum.ToArray())));
            supportedBasePages.Add(page); disclosedOwners.Add(("base", page.PageId));
            if (!_options.ShowBaseModule) continue;
            foreach (StudioMethodBinding method in pageMethods) disclosedMethodIds.Add(method.RegisteredMethodId);
            foreach ((StudioResourceRegistration registration, StudioMethodBinding method) in pageResources)
            {
                disclosedMethodIds.Add(method.RegisteredMethodId); disclosedOwners.Add(("base", registration.ResolverId));
                if (!visibleResolvers.Any(value => value.Kind == registration.Kind))
                    visibleResolvers.Add(new StudioVisibleResourceResolver("base", registration.Kind, registration.ResolverId,
                        StudioSha256.FromDigest(registration.Checksum.ToArray())));
            }
        }
        if (!supportedBasePages.Any(static value => value.PageId == "base.overview"))
            throw new InvalidOperationException("The required BASE overview Runtime contribution is incomplete.");
        var visibleModules = new List<StudioVisibleModule>();
        var visibleClients = new List<StudioVisibleClient>();
        var visibleCommands = new List<StudioVisibleCommand>();
        foreach (StudioModuleRegistration module in invocation.ApplicationGraph.Modules)
        {
            if (!await AdmittedAsync(invocation, module.Grants, cancellationToken).ConfigureAwait(false)) continue;
            StudioPageRegistration[] admittedPages = module.Identity.ModuleId == "base" ?
                (_options.ShowBaseModule ? supportedBasePages.ToArray() : []) :
                (await FilterPagesAsync(invocation, module.Pages, cancellationToken).ConfigureAwait(false)).ToArray();
            StudioModuleRuntimeContribution? moduleRuntime = module.Identity.ModuleId == "base" ? contribution :
                _runtime.Contributions.SingleOrDefault(value => StringComparer.Ordinal.Equals(value.ModuleId, module.Identity.ModuleId));
            if (module.Identity.ModuleId != "base")
            {
                var executable = new List<StudioPageRegistration>();
                foreach (StudioPageRegistration page in admittedPages)
                {
                    string[] viewIds = page.Presentation.Sections.SelectMany(static value => value.ViewIds).ToArray();
                    StudioMethodBinding[] pageMethods = moduleRuntime?.Methods.Where(value => value.Kind == StudioMethodKind.Page &&
                        StringComparer.Ordinal.Equals(value.OwningPageOrCommandId, page.PageId)).ToArray() ?? [];
                    if (pageMethods.Length != viewIds.Length || viewIds.Any(viewId =>
                            pageMethods.Count(method => method.RegisteredMethodId.EndsWith(viewId, StringComparison.Ordinal)) != 1)) continue;
                    bool hasParameterizedRoute = page.Route.Segments.Any(static value => value.Kind == StudioRouteSegmentKind.Parameter);
                    StudioResourceIdentity? initial = hasParameterizedRoute || page.AcceptedResources.IsEmpty ? null :
                        page.AcceptedResources.Contains(StudioResourceKind.Application)
                            ? new StudioApplicationResource(invocation.ApplicationGraph.ApplicationId) : null;
                    if (!hasParameterizedRoute && !page.AcceptedResources.IsEmpty && initial is null) continue;
                    var resolverMethods = new List<string>(); bool complete = true;
                    foreach (StudioResourceKind kind in page.AcceptedResources)
                    {
                        StudioResourceRegistration? registration = module.Resources.SingleOrDefault(value => value.Kind == kind) ??
                            invocation.ApplicationGraph.Modules.SelectMany(static value => value.Resources)
                                .FirstOrDefault(value => value.Kind == kind);
                        StudioMethodBinding? method = registration is null ? null :
                            moduleRuntime?.Methods.SingleOrDefault(value => value.Kind == StudioMethodKind.Resolve &&
                                StringComparer.Ordinal.Equals(value.OwningPageOrCommandId, registration.ResolverId)) ??
                            _runtime.Contributions.SelectMany(static value => value.Methods)
                                .SingleOrDefault(value => value.Kind == StudioMethodKind.Resolve &&
                                    StringComparer.Ordinal.Equals(value.OwningPageOrCommandId, registration.ResolverId));
                        if (registration is null || method is null ||
                            !await AdmittedAsync(invocation, registration.Grants, cancellationToken).ConfigureAwait(false))
                        { complete = false; break; }
                        StudioModuleRuntimeContribution resolverRuntime = _runtime.Contributions.Single(value =>
                            value.Methods.Contains(method));
                        StudioModuleRegistration resolverModule = invocation.ApplicationGraph.Modules.Single(value =>
                            StringComparer.Ordinal.Equals(value.Identity.ModuleId, resolverRuntime.ModuleId));
                        resolverMethods.Add(method.RegisteredMethodId);
                        disclosedMethodIds.Add(method.RegisteredMethodId);
                        disclosedOwners.Add((resolverModule.Identity.ModuleId, registration.ResolverId));
                        if (!visibleResolvers.Any(value => value.ModuleId == resolverModule.Identity.ModuleId &&
                                value.Kind == registration.Kind && StringComparer.Ordinal.Equals(value.ResolverId, registration.ResolverId)))
                            visibleResolvers.Add(new StudioVisibleResourceResolver(resolverModule.Identity.ModuleId, registration.Kind,
                                registration.ResolverId, StudioSha256.FromDigest(registration.Checksum.ToArray())));
                    }
                    if (!complete) continue;
                    StudioVisibleView[] pageViews = viewIds.Select(viewId =>
                    {
                        StudioViewRegistration view = module.Views.Single(value => StringComparer.Ordinal.Equals(value.ViewId, viewId));
                        StudioMethodBinding method = pageMethods.Single(value => value.RegisteredMethodId.EndsWith(viewId, StringComparison.Ordinal));
                        return new StudioVisibleView(view.ViewId, view.Version, method.RegisteredMethodId, view.ItemKind,
                            view.ItemNodeId, StudioSha256.FromDigest(view.ItemNodeChecksum.ToArray()), view.Presentation,
                            StudioSha256.FromDigest(view.Checksum.ToArray()));
                    }).OrderBy(static value => value.ViewId, StringComparer.Ordinal).ToArray();
                    visiblePages.Add(StudioVisiblePage.Create(module.Identity.ModuleId, page.PageId, page.Version, page.Area,
                        page.Presentation.NavigationRole, page.Route, page.AcceptedResources,
                        pageMethods.Select(static value => value.RegisteredMethodId).Order(StringComparer.Ordinal),
                        resolverMethods.Order(StringComparer.Ordinal), initial, page.Presentation, pageViews,
                        StudioSha256.FromDigest(page.Checksum.ToArray())));
                    foreach (StudioMethodBinding method in pageMethods) disclosedMethodIds.Add(method.RegisteredMethodId);
                    if (moduleRuntime is not null)
                        foreach (StudioMethodBinding method in moduleRuntime.Methods.Where(value =>
                                     value.Kind == StudioMethodKind.InvalidationSubscribe &&
                                     StringComparer.Ordinal.Equals(value.OwningPageOrCommandId, page.PageId)))
                            disclosedMethodIds.Add(method.RegisteredMethodId);
                    disclosedOwners.Add((module.Identity.ModuleId, page.PageId));
                    executable.Add(page);
                }
                admittedPages = executable.ToArray();
            }
            if (admittedPages.Length == 0) continue;
            visibleModules.Add(new StudioVisibleModule(module.Identity.ModuleId, module.Identity.Version, module.DisplayNameMessageId,
                module.Asset.Necessity, StudioSha256.FromDigest(module.Identity.Checksum.ToArray()),
                StudioSha256.FromDigest(module.Frontend.FrontendAbiChecksum.ToArray()),
                StudioSha256.FromDigest(module.Asset.AssetGraphChecksum.ToArray())));
            HashSet<string> admittedPageIds = admittedPages.Select(static value => value.PageId).ToHashSet(StringComparer.Ordinal);
            if (_options.Mode == StudioMode.Operate && moduleRuntime is not null)
            {
                foreach (StudioCommandRegistration command in module.Commands)
                {
                    StudioPageRegistration[] owners = admittedPages.Where(page => page.Presentation.Sections.Any(section => section.CommandIds.Contains(command.CommandId))).ToArray();
                    StudioMethodBinding? preview = moduleRuntime.Methods.SingleOrDefault(method => method.Kind == StudioMethodKind.Preview &&
                        StringComparer.Ordinal.Equals(method.OwningPageOrCommandId, command.CommandId));
                    StudioMethodBinding? execute = moduleRuntime.Methods.SingleOrDefault(method => method.Kind == StudioMethodKind.Execute &&
                        StringComparer.Ordinal.Equals(method.OwningPageOrCommandId, command.CommandId));
                    if (owners.Length == 0 || preview is null || execute is null ||
                        !await AdmittedAsync(invocation, command.Grants, cancellationToken).ConfigureAwait(false)) continue;
                    if (command.FreshAuthentication is { } assurance && !_authentication.Integration.Descriptor.SupportedFreshAuthentication.Contains(assurance)) continue;
                    StudioResourceKind[] resources = owners.SelectMany(static page => page.AcceptedResources).Distinct().OrderBy(static kind => (byte)kind).ToArray();
                    visibleCommands.Add(StudioVisibleCommand.Create(module.Identity.ModuleId, command.CommandId, command.Version,
                        command.ActionClass, owners.Select(static page => page.PageId).Order(StringComparer.Ordinal), resources,
                        StudioSha256.FromDigest(command.Checksum.ToArray())));
                    disclosedOwners.Add((module.Identity.ModuleId, command.CommandId)); disclosedMethodIds.Add(preview.RegisteredMethodId); disclosedMethodIds.Add(execute.RegisteredMethodId);
                }
            }
            foreach (StudioFrameworkClientRegistration client in module.Clients
                         .Where(client => client.OwningPageIds.Any(admittedPageIds.Contains)))
            {
                ImmutableArray<StudioFrameworkSurfaceOperation> operations = [];
                if (client.Protocol == StudioFrameworkClientProtocol.FrameworkGeneratedContractV1)
                {
                    IStudioFrameworkEndpointSurface surface = _frameworkSurfaces.SingleOrDefault(value =>
                        StringComparer.Ordinal.Equals(value.EndpointSurfaceId, client.EndpointSurfaceId)) ??
                        throw new InvalidOperationException("base.studio.frameworkSurfaceMissing");
                    if (!StudioSha256.FixedTimeEquals(surface.OperationInventoryChecksum, client.OperationInventoryChecksum))
                        throw new InvalidOperationException("base.studio.frameworkSurfaceSubstituted");
                    operations = surface.Operations;
                }
                visibleClients.Add(new StudioVisibleClient(module.Identity.ModuleId, client.ClientId, client.Version,
                    client.Protocol, StudioSha256.FromDigest(client.StaticRuntimeAbiChecksum.ToArray()),
                    StudioSha256.FromDigest(client.GeneratedContractChecksum.ToArray()),
                    StudioSha256.FromDigest(client.OperationInventoryChecksum.ToArray()), client.EndpointSurfaceId,
                    client.TransportClass, client.OwningPageIds.Where(admittedPageIds.Contains).ToImmutableArray(), client.Limits, operations));
            }
        }
        StudioModuleRuntimeContribution[] runtimeContributions = _runtime.Contributions.ToArray();
        StudioMethodBinding[] methods = runtimeContributions.SelectMany(static value => value.Methods)
            .Where(value => disclosedMethodIds.Contains(value.RegisteredMethodId)).ToArray();
        HashSet<string> endpointIds = methods.Select(static value => value.EndpointId).ToHashSet(StringComparer.Ordinal);
        StudioEndpointContract[] endpoints = runtimeContributions.SelectMany(static value => value.Endpoints)
            .Where(value => endpointIds.Contains(value.EndpointId)).ToArray();
        HashSet<string> typeIds = endpoints.SelectMany(static endpoint => new[] { endpoint.RequestNodeId, endpoint.ResultNodeId, endpoint.ErrorNodeId }).ToHashSet(StringComparer.Ordinal);
        StudioNamedTypeContract[] availableTypes = runtimeContributions.SelectMany(static value => value.Types).ToArray();
        bool changed; do { changed = false; foreach (StudioNamedTypeContract type in availableTypes.Where(value => typeIds.Contains(value.TypeId)))
            foreach (string reference in type.References) changed |= typeIds.Add(reference); } while (changed);
        StudioNamedTypeContract[] types = availableTypes.Where(value => typeIds.Contains(value.TypeId)).ToArray();
        ValidateGridPropertyCorrespondence(visiblePages, types);
        StudioContractMap map = StudioContractMap.Create("base-json-v1", "base-json-v1", "base-errors-v1", "base-studio-realtime-v1",
            baseClient.StaticRuntimeAbiChecksum, baseClient.GeneratedContractChecksum, types, endpoints, methods, disclosedOwners);
        DateTimeOffset now = _timeProvider.GetUtcNow();
        StudioDynamicStoreAuthorityRequest storeRequest = StoreAuthorityRequest(invocation.ApplicationGraph.ApplicationId);
        OperationResult<StudioDynamicStoreAuthority>? dynamicStore = await CaptureStoreAsync(_storeAuthority, _lateWork, storeRequest, cancellationToken).ConfigureAwait(false);
        if (dynamicStore is null) return null;
        if (!dynamicStore.IsSuccess() || dynamicStore.Value is null || !StudioDynamicStoreAuthorityContract.IsValidResult(storeRequest, dynamicStore.Value)) return null;
        StudioStoreAuthority store = StudioStoreAuthority.Create(dynamicStore.Value.StoreInstanceId, _baseAuthority.ProviderGeneration,
            dynamicStore.Value.RestoreEpoch, dynamicStore.Value.SchemaGeneration, StudioSha256.FromDigest(_baseAuthority.GetProviderCapabilityChecksum()));
        StudioResponseAuthority response = StudioResponseAuthority.Create(
            invocation.Authorization.Session.PrincipalGeneration, invocation.Authorization.Session.SessionChecksum,
            invocation.Authorization.Session.ProtectedScopeChecksum, invocation.ApplicationGraph.Generation,
            invocation.ApplicationGraph.Checksum, invocation.ApplicationGraph.Generation,
            StudioSha256.FromDigest(_baseAuthority.GetChecksum()), _baseAuthority.PolicyOwnerGeneration,
            StudioSha256.FromDigest(_baseAuthority.GetPolicyOwnerChecksum()), [store], now,
            invocation.Authorization.Session.ExpiresAtUtc, [invocation.Authorization.AuthorizedThroughUtc]);
        StudioShellLimits limits = StudioShellLimits.Create(64, 512, 256, 128, 32,
            4 * 1024 * 1024, 16 * 1024 * 1024, TimeSpan.FromSeconds(10));
        return StudioBootstrapSnapshot.Create(invocation.ApplicationGraph.ApplicationId, _options.Mode,
            response, visibleModules.OrderBy(static value => value.ModuleId, StringComparer.Ordinal).ThenBy(static value => value.Version),
            visiblePages.OrderBy(static value => (byte)value.Area).ThenBy(static value => value.ModuleId, StringComparer.Ordinal).ThenBy(static value => value.PageId, StringComparer.Ordinal),
            visibleCommands.OrderBy(static value => value.ModuleId, StringComparer.Ordinal).ThenBy(static value => value.CommandId, StringComparer.Ordinal), visibleResolvers.OrderBy(static value => value.ModuleId, StringComparer.Ordinal).ThenBy(static value => (byte)value.Kind).ThenBy(static value => value.ResolverId, StringComparer.Ordinal), [],
            visibleClients.OrderBy(static value => value.ModuleId, StringComparer.Ordinal).ThenBy(static value => value.ClientId, StringComparer.Ordinal), map, limits, now,
            response.AuthorizedThroughUtc);
    }

    private static void ValidateGridPropertyCorrespondence(IEnumerable<StudioVisiblePage> pages,
        IEnumerable<StudioNamedTypeContract> types)
    {
        Dictionary<string, StudioNamedTypeContract> nodes = types.ToDictionary(static value => value.TypeId, StringComparer.Ordinal);
        foreach (StudioVisibleView view in pages.SelectMany(static page => page.Views))
        {
            StudioGridDefinition? grid = view.Presentation.Grid; if (grid is null) continue;
            if (!StringComparer.Ordinal.Equals(grid.RowNodeId, view.ItemNodeId) ||
                !nodes.TryGetValue(grid.RowNodeId, out StudioNamedTypeContract? node) ||
                !StudioSha256.FixedTimeEquals(node.NodeChecksum, grid.RowNodeChecksum) ||
                !StudioSha256.FixedTimeEquals(node.NodeChecksum, view.ItemNodeChecksum))
                throw new InvalidOperationException("A Studio grid row node differs from its executable L41 node.");
            using System.Text.Json.JsonDocument document = System.Text.Json.JsonDocument.Parse(node.GetCanonicalDescriptor());
            System.Text.Json.JsonElement root = document.RootElement;
            if (root.GetProperty("kind").GetString() != "object")
                throw new InvalidOperationException("A Studio grid row node is not a closed object.");
            HashSet<string> properties = root.GetProperty("properties").EnumerateArray()
                .Select(static property => property.GetProperty("name").GetString()!).ToHashSet(StringComparer.Ordinal);
            if (grid.Columns.Any(column => !properties.Contains(column.StablePropertyOrEdgeId)))
                throw new InvalidOperationException("A Studio grid column does not correspond to its executable L41 row property.");
        }
    }

    private async ValueTask<bool> AdmittedAsync(StudioBootstrapInvocation invocation,
        IEnumerable<StudioGrantRequirement> grants, CancellationToken cancellationToken)
    {
        foreach (StudioGrantRequirement grant in grants)
            if (await _authorization.AdmitAsync(invocation, grant, cancellationToken).ConfigureAwait(false) is null) return false;
        return true;
    }

    private async ValueTask<List<StudioPageRegistration>> FilterPagesAsync(StudioBootstrapInvocation invocation,
        IEnumerable<StudioPageRegistration> pages, CancellationToken cancellationToken)
    {
        var result = new List<StudioPageRegistration>();
        foreach (StudioPageRegistration page in pages)
            if (await AdmittedAsync(invocation, page.Grants, cancellationToken).ConfigureAwait(false)) result.Add(page);
        return result;
    }

    internal static StudioDynamicStoreAuthorityRequest StoreAuthorityRequest(string applicationId) => new()
    { ApplicationId = applicationId, MaximumEvidenceBytes = 4_096, MaximumTransientBytes = 16_384, Deadline = TimeSpan.FromSeconds(2) };

    internal static async ValueTask<OperationResult<StudioDynamicStoreAuthority>?> CaptureStoreAsync(
        IStudioDynamicStoreAuthoritySource source, StudioLateWorkRegistry lateWork,
        StudioDynamicStoreAuthorityRequest request, CancellationToken cancellationToken)
    {
        if (!lateWork.TryEnter(out StudioLateWorkLease lease)) return null;
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken); deadline.CancelAfter(request.Deadline);
        Task<OperationResult<StudioDynamicStoreAuthority>> task;
        try { task = source.CaptureStudioDynamicStoreAuthorityAsync(request, deadline.Token).AsTask(); }
        catch { lease.Dispose(); throw; }
        try { OperationResult<StudioDynamicStoreAuthority> result = await task.WaitAsync(deadline.Token).ConfigureAwait(false); lease.Dispose(); return result; }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { lease.Retain(task); return null; }
        catch (OperationCanceledException) { lease.Retain(task); throw; }
        catch { lease.Dispose(); throw; }
    }

}
