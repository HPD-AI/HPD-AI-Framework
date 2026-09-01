using HPD.AI.Platform.Studio;
using System.Collections.Immutable;

namespace HPD.Base.Studio;

internal sealed class BaseStudioBootstrapRuntime : IBaseStudioBootstrapRuntime
{
    private readonly BaseStudioAuthorization _authorization;
    private readonly BaseStudioRuntimeCatalog _runtime;
    private readonly HPDBaseStudioAuthoritySnapshot _baseAuthority;
    private readonly IBaseStudioDynamicStoreAuthoritySource _storeAuthority;
    private readonly ImmutableArray<IBaseStudioFrameworkEndpointSurface> _frameworkSurfaces;
    private readonly BaseStudioLateWorkRegistry _lateWork;
    private readonly TimeProvider _timeProvider;
    private readonly HPDBaseStudioOptions _options;
    private readonly BaseStudioAuthenticationProvider _authentication;

    public BaseStudioBootstrapRuntime(BaseStudioAuthorization authorization, BaseStudioRuntimeCatalog runtime,
        HPDBaseStudioAuthoritySnapshot baseAuthority, IRecordStore recordStore,
        IRecordMutationStore mutationStore, IAtomicRecordStore atomicStore,
        IEnumerable<IBaseStudioFrameworkEndpointSurface> frameworkSurfaces,
        BaseStudioLateWorkRegistry lateWork, TimeProvider timeProvider, HPDBaseStudioOptions options,
        BaseStudioAuthenticationProvider authentication)
    { _authorization = authorization; _runtime = runtime; _baseAuthority = baseAuthority;
      if (!ReferenceEquals(recordStore, atomicStore) || !ReferenceEquals(mutationStore, atomicStore))
          throw new InvalidOperationException("base.studio.installedStoreAuthoritySubstituted");
      _storeAuthority = atomicStore as IBaseStudioDynamicStoreAuthoritySource ??
          throw new InvalidOperationException("base.studio.installedStoreAuthorityUnavailable");
      _frameworkSurfaces = frameworkSurfaces.ToImmutableArray();
      _lateWork = lateWork; _timeProvider = timeProvider; _options = options; _authentication = authentication; }

    public async ValueTask<BaseStudioBootstrapSnapshot?> CreateAsync(BaseStudioBootstrapInvocation invocation,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(invocation);
        BaseStudioModuleRegistration baseModule = invocation.ApplicationGraph.Modules.Single(static value => value.Identity.ModuleId == "base");
        BaseStudioModuleRuntimeContribution contribution = _runtime.Contributions.Single(static value => value.ModuleId == "base");
        BaseStudioFrameworkClientRegistration baseClient = baseModule.Clients.Single(static value => value.ClientId == "base.control-plane");
        if (!BaseStudioSha256.FixedTimeEquals(invocation.Request.RuntimeClientChecksum, baseClient.StaticRuntimeAbiChecksum) ||
            await _authorization.ResolvePrincipalAsync(invocation, cancellationToken).ConfigureAwait(false) is null)
            return null;
        if (!await AdmittedAsync(invocation, baseModule.Grants, cancellationToken).ConfigureAwait(false)) return null;

        var visiblePages = new List<BaseStudioVisiblePage>();
        var visibleResolvers = new List<BaseStudioVisibleResourceResolver>();
        var disclosedMethodIds = new HashSet<string>(StringComparer.Ordinal);
        var disclosedOwners = new HashSet<(string ModuleId, string OwnerId)>();
        var supportedBasePages = new List<BaseStudioPageRegistration>();
        foreach (BaseStudioPageRegistration page in baseModule.Pages)
        {
            string[] viewIds = page.Presentation.Sections.SelectMany(static value => value.ViewIds).ToArray();
            BaseStudioMethodBinding[] pageMethods = contribution.Methods.Where(value => value.Kind == BaseStudioMethodKind.Page &&
                StringComparer.Ordinal.Equals(value.OwningPageOrCommandId, page.PageId)).ToArray();
            if (pageMethods.Length != viewIds.Length || viewIds.Any(viewId =>
                    pageMethods.Count(method => method.RegisteredMethodId.EndsWith(viewId, StringComparison.Ordinal)) != 1)) continue;
            var pageResources = new List<(BaseStudioResourceRegistration Registration, BaseStudioMethodBinding Method)>();
            bool complete = true;
            foreach (BaseStudioResourceKind kind in page.AcceptedResources)
            {
                BaseStudioResourceRegistration registration = baseModule.Resources.Single(value => value.Kind == kind);
                BaseStudioMethodBinding? method = contribution.Methods.SingleOrDefault(value => value.Kind == BaseStudioMethodKind.Resolve &&
                    StringComparer.Ordinal.Equals(value.OwningPageOrCommandId, registration.ResolverId));
                if (method is null || !await AdmittedAsync(invocation, registration.Grants, cancellationToken).ConfigureAwait(false))
                { complete = false; break; }
                pageResources.Add((registration, method));
            }
            if (!complete || !await AdmittedAsync(invocation, page.Grants, cancellationToken).ConfigureAwait(false)) continue;
            BaseStudioResourceIdentity? initial = page.Route.Segments.Any(static value => value.Kind == BaseStudioRouteSegmentKind.Parameter)
                ? null : page.AcceptedResources.Contains(BaseStudioResourceKind.Application)
                    ? new BaseStudioApplicationResource(invocation.ApplicationGraph.ApplicationId) : null;
            if (initial is null && !page.Route.Segments.Any(static value => value.Kind == BaseStudioRouteSegmentKind.Parameter) && !page.AcceptedResources.IsEmpty)
                continue;
            string[] observations = pageMethods.Select(static value => value.RegisteredMethodId).Order(StringComparer.Ordinal).ToArray();
            string[] resolverMethods = pageResources.Select(static value => value.Method.RegisteredMethodId).Order(StringComparer.Ordinal).ToArray();
            BaseStudioVisibleView[] views = viewIds.Select(viewId =>
            {
                BaseStudioViewRegistration view = baseModule.Views.Single(value => StringComparer.Ordinal.Equals(value.ViewId, viewId));
                BaseStudioMethodBinding method = pageMethods.Single(value => value.RegisteredMethodId.EndsWith(viewId, StringComparison.Ordinal));
                return new BaseStudioVisibleView(view.ViewId, view.Version, method.RegisteredMethodId, view.ItemKind, view.ItemNodeId,
                    BaseStudioSha256.FromDigest(view.ItemNodeChecksum.ToArray()), view.Presentation,
                    BaseStudioSha256.FromDigest(view.Checksum.ToArray()));
            }).OrderBy(static value => value.ViewId, StringComparer.Ordinal).ToArray();
            visiblePages.Add(BaseStudioVisiblePage.Create("base", page.PageId, page.Version, page.Area,
                page.Presentation.NavigationRole, page.Route, page.AcceptedResources, observations, resolverMethods,
                initial, page.Presentation, views, BaseStudioSha256.FromDigest(page.Checksum.ToArray())));
            supportedBasePages.Add(page); disclosedOwners.Add(("base", page.PageId));
            foreach (BaseStudioMethodBinding method in pageMethods) disclosedMethodIds.Add(method.RegisteredMethodId);
            foreach ((BaseStudioResourceRegistration registration, BaseStudioMethodBinding method) in pageResources)
            {
                disclosedMethodIds.Add(method.RegisteredMethodId); disclosedOwners.Add(("base", registration.ResolverId));
                if (!visibleResolvers.Any(value => value.Kind == registration.Kind))
                    visibleResolvers.Add(new BaseStudioVisibleResourceResolver("base", registration.Kind, registration.ResolverId,
                        BaseStudioSha256.FromDigest(registration.Checksum.ToArray())));
            }
        }
        if (!supportedBasePages.Any(static value => value.PageId == "base.overview"))
            throw new InvalidOperationException("The required BASE overview Runtime contribution is incomplete.");
        var visibleModules = new List<BaseStudioVisibleModule>();
        var visibleClients = new List<BaseStudioVisibleClient>();
        var visibleCommands = new List<BaseStudioVisibleCommand>();
        foreach (BaseStudioModuleRegistration module in invocation.ApplicationGraph.Modules)
        {
            if (!await AdmittedAsync(invocation, module.Grants, cancellationToken).ConfigureAwait(false)) continue;
            BaseStudioPageRegistration[] admittedPages = module.Identity.ModuleId == "base" ? supportedBasePages.ToArray() :
                (await FilterPagesAsync(invocation, module.Pages, cancellationToken).ConfigureAwait(false)).ToArray();
            BaseStudioModuleRuntimeContribution? moduleRuntime = module.Identity.ModuleId == "base" ? contribution :
                _runtime.Contributions.SingleOrDefault(value => StringComparer.Ordinal.Equals(value.ModuleId, module.Identity.ModuleId));
            if (module.Identity.ModuleId != "base")
            {
                var executable = new List<BaseStudioPageRegistration>();
                foreach (BaseStudioPageRegistration page in admittedPages)
                {
                    string[] viewIds = page.Presentation.Sections.SelectMany(static value => value.ViewIds).ToArray();
                    BaseStudioMethodBinding[] pageMethods = moduleRuntime?.Methods.Where(value => value.Kind == BaseStudioMethodKind.Page &&
                        StringComparer.Ordinal.Equals(value.OwningPageOrCommandId, page.PageId)).ToArray() ?? [];
                    if (pageMethods.Length != viewIds.Length || viewIds.Any(viewId =>
                            pageMethods.Count(method => method.RegisteredMethodId.EndsWith(viewId, StringComparison.Ordinal)) != 1)) continue;
                    bool hasParameterizedRoute = page.Route.Segments.Any(static value => value.Kind == BaseStudioRouteSegmentKind.Parameter);
                    BaseStudioResourceIdentity? initial = hasParameterizedRoute || page.AcceptedResources.IsEmpty ? null :
                        page.AcceptedResources.Contains(BaseStudioResourceKind.Application)
                            ? new BaseStudioApplicationResource(invocation.ApplicationGraph.ApplicationId) : null;
                    if (!hasParameterizedRoute && !page.AcceptedResources.IsEmpty && initial is null) continue;
                    var resolverMethods = new List<string>(); bool complete = true;
                    foreach (BaseStudioResourceKind kind in page.AcceptedResources)
                    {
                        BaseStudioResourceRegistration registration = module.Resources.Single(value => value.Kind == kind);
                        BaseStudioMethodBinding? method = moduleRuntime?.Methods.SingleOrDefault(value => value.Kind == BaseStudioMethodKind.Resolve &&
                            StringComparer.Ordinal.Equals(value.OwningPageOrCommandId, registration.ResolverId));
                        if (method is null || !await AdmittedAsync(invocation, registration.Grants, cancellationToken).ConfigureAwait(false))
                        { complete = false; break; }
                        resolverMethods.Add(method.RegisteredMethodId);
                        disclosedMethodIds.Add(method.RegisteredMethodId);
                        disclosedOwners.Add((module.Identity.ModuleId, registration.ResolverId));
                        if (!visibleResolvers.Any(value => value.ModuleId == module.Identity.ModuleId && value.Kind == registration.Kind))
                            visibleResolvers.Add(new BaseStudioVisibleResourceResolver(module.Identity.ModuleId, registration.Kind,
                                registration.ResolverId, BaseStudioSha256.FromDigest(registration.Checksum.ToArray())));
                    }
                    if (!complete) continue;
                    BaseStudioVisibleView[] pageViews = viewIds.Select(viewId =>
                    {
                        BaseStudioViewRegistration view = module.Views.Single(value => StringComparer.Ordinal.Equals(value.ViewId, viewId));
                        BaseStudioMethodBinding method = pageMethods.Single(value => value.RegisteredMethodId.EndsWith(viewId, StringComparison.Ordinal));
                        return new BaseStudioVisibleView(view.ViewId, view.Version, method.RegisteredMethodId, view.ItemKind,
                            view.ItemNodeId, BaseStudioSha256.FromDigest(view.ItemNodeChecksum.ToArray()), view.Presentation,
                            BaseStudioSha256.FromDigest(view.Checksum.ToArray()));
                    }).OrderBy(static value => value.ViewId, StringComparer.Ordinal).ToArray();
                    visiblePages.Add(BaseStudioVisiblePage.Create(module.Identity.ModuleId, page.PageId, page.Version, page.Area,
                        page.Presentation.NavigationRole, page.Route, page.AcceptedResources,
                        pageMethods.Select(static value => value.RegisteredMethodId).Order(StringComparer.Ordinal),
                        resolverMethods.Order(StringComparer.Ordinal), initial, page.Presentation, pageViews,
                        BaseStudioSha256.FromDigest(page.Checksum.ToArray())));
                    foreach (BaseStudioMethodBinding method in pageMethods) disclosedMethodIds.Add(method.RegisteredMethodId);
                    disclosedOwners.Add((module.Identity.ModuleId, page.PageId));
                    executable.Add(page);
                }
                admittedPages = executable.ToArray();
            }
            if (admittedPages.Length == 0) continue;
            visibleModules.Add(new BaseStudioVisibleModule(module.Identity.ModuleId, module.Identity.Version, module.DisplayNameMessageId,
                module.Asset.Necessity, BaseStudioSha256.FromDigest(module.Identity.Checksum.ToArray()),
                BaseStudioSha256.FromDigest(module.Frontend.FrontendAbiChecksum.ToArray()),
                BaseStudioSha256.FromDigest(module.Asset.AssetGraphChecksum.ToArray())));
            HashSet<string> admittedPageIds = admittedPages.Select(static value => value.PageId).ToHashSet(StringComparer.Ordinal);
            if (_options.Mode == BaseStudioMode.Operate && moduleRuntime is not null)
            {
                foreach (BaseStudioCommandRegistration command in module.Commands)
                {
                    BaseStudioPageRegistration[] owners = admittedPages.Where(page => page.Presentation.Sections.Any(section => section.CommandIds.Contains(command.CommandId))).ToArray();
                    BaseStudioMethodBinding? preview = moduleRuntime.Methods.SingleOrDefault(method => method.Kind == BaseStudioMethodKind.Preview &&
                        StringComparer.Ordinal.Equals(method.OwningPageOrCommandId, command.CommandId));
                    BaseStudioMethodBinding? execute = moduleRuntime.Methods.SingleOrDefault(method => method.Kind == BaseStudioMethodKind.Execute &&
                        StringComparer.Ordinal.Equals(method.OwningPageOrCommandId, command.CommandId));
                    if (owners.Length == 0 || preview is null || execute is null || !await AdmittedAsync(invocation, command.Grants, cancellationToken).ConfigureAwait(false)) continue;
                    if (command.FreshAuthentication is { } assurance && !_authentication.Integration.Descriptor.SupportedFreshAuthentication.Contains(assurance)) continue;
                    BaseStudioResourceKind[] resources = owners.SelectMany(static page => page.AcceptedResources).Distinct().OrderBy(static kind => (byte)kind).ToArray();
                    visibleCommands.Add(BaseStudioVisibleCommand.Create(module.Identity.ModuleId, command.CommandId, command.Version,
                        command.ActionClass, owners.Select(static page => page.PageId).Order(StringComparer.Ordinal), resources,
                        BaseStudioSha256.FromDigest(command.Checksum.ToArray())));
                    disclosedOwners.Add((module.Identity.ModuleId, command.CommandId)); disclosedMethodIds.Add(preview.RegisteredMethodId); disclosedMethodIds.Add(execute.RegisteredMethodId);
                }
            }
            foreach (BaseStudioFrameworkClientRegistration client in module.Clients
                         .Where(client => client.OwningPageIds.Any(admittedPageIds.Contains)))
            {
                ImmutableArray<BaseStudioFrameworkSurfaceOperation> operations = [];
                if (client.Protocol == BaseStudioFrameworkClientProtocol.FrameworkGeneratedContractV1)
                {
                    IBaseStudioFrameworkEndpointSurface surface = _frameworkSurfaces.SingleOrDefault(value =>
                        StringComparer.Ordinal.Equals(value.EndpointSurfaceId, client.EndpointSurfaceId)) ??
                        throw new InvalidOperationException("base.studio.frameworkSurfaceMissing");
                    if (!BaseStudioSha256.FixedTimeEquals(surface.OperationInventoryChecksum, client.OperationInventoryChecksum))
                        throw new InvalidOperationException("base.studio.frameworkSurfaceSubstituted");
                    operations = surface.Operations;
                }
                visibleClients.Add(new BaseStudioVisibleClient(module.Identity.ModuleId, client.ClientId, client.Version,
                    client.Protocol, BaseStudioSha256.FromDigest(client.StaticRuntimeAbiChecksum.ToArray()),
                    BaseStudioSha256.FromDigest(client.GeneratedContractChecksum.ToArray()),
                    BaseStudioSha256.FromDigest(client.OperationInventoryChecksum.ToArray()), client.EndpointSurfaceId,
                    client.TransportClass, client.OwningPageIds.Where(admittedPageIds.Contains).ToImmutableArray(), client.Limits, operations));
            }
        }
        BaseStudioModuleRuntimeContribution[] runtimeContributions = _runtime.Contributions.ToArray();
        BaseStudioMethodBinding[] methods = runtimeContributions.SelectMany(static value => value.Methods)
            .Where(value => disclosedMethodIds.Contains(value.RegisteredMethodId)).ToArray();
        HashSet<string> endpointIds = methods.Select(static value => value.EndpointId).ToHashSet(StringComparer.Ordinal);
        BaseStudioEndpointContract[] endpoints = runtimeContributions.SelectMany(static value => value.Endpoints)
            .Where(value => endpointIds.Contains(value.EndpointId)).ToArray();
        HashSet<string> typeIds = endpoints.SelectMany(static endpoint => new[] { endpoint.RequestNodeId, endpoint.ResultNodeId, endpoint.ErrorNodeId }).ToHashSet(StringComparer.Ordinal);
        BaseStudioNamedTypeContract[] availableTypes = runtimeContributions.SelectMany(static value => value.Types).ToArray();
        bool changed; do { changed = false; foreach (BaseStudioNamedTypeContract type in availableTypes.Where(value => typeIds.Contains(value.TypeId)))
            foreach (string reference in type.References) changed |= typeIds.Add(reference); } while (changed);
        BaseStudioNamedTypeContract[] types = availableTypes.Where(value => typeIds.Contains(value.TypeId)).ToArray();
        ValidateGridPropertyCorrespondence(visiblePages, types);
        BaseStudioContractMap map = BaseStudioContractMap.Create("base-json-v1", "base-json-v1", "base-errors-v1", "base-studio-realtime-v1",
            baseClient.StaticRuntimeAbiChecksum, baseClient.GeneratedContractChecksum, types, endpoints, methods, disclosedOwners);
        DateTimeOffset now = _timeProvider.GetUtcNow();
        BaseStudioDynamicStoreAuthorityRequest storeRequest = StoreAuthorityRequest(invocation.ApplicationGraph.ApplicationId);
        OperationResult<BaseStudioDynamicStoreAuthority>? dynamicStore = await CaptureStoreAsync(_storeAuthority, _lateWork, storeRequest, cancellationToken).ConfigureAwait(false);
        if (dynamicStore is null) return null;
        if (!dynamicStore.IsSuccess() || dynamicStore.Value is null || !BaseStudioDynamicStoreAuthorityContract.IsValidResult(storeRequest, dynamicStore.Value)) return null;
        BaseStudioStoreAuthority store = BaseStudioStoreAuthority.Create(dynamicStore.Value.StoreInstanceId, _baseAuthority.ProviderGeneration,
            dynamicStore.Value.RestoreEpoch, dynamicStore.Value.SchemaGeneration, BaseStudioSha256.FromDigest(_baseAuthority.GetProviderCapabilityChecksum()));
        BaseStudioResponseAuthority response = BaseStudioResponseAuthority.Create(
            invocation.Authorization.Session.PrincipalGeneration, invocation.Authorization.Session.SessionChecksum,
            invocation.Authorization.Session.ProtectedScopeChecksum, invocation.ApplicationGraph.Generation,
            invocation.ApplicationGraph.Checksum, invocation.ApplicationGraph.Generation,
            BaseStudioSha256.FromDigest(_baseAuthority.GetChecksum()), _baseAuthority.PolicyOwnerGeneration,
            BaseStudioSha256.FromDigest(_baseAuthority.GetPolicyOwnerChecksum()), [store], now,
            invocation.Authorization.Session.ExpiresAtUtc, [invocation.Authorization.AuthorizedThroughUtc]);
        BaseStudioShellLimits limits = BaseStudioShellLimits.Create(64, 512, 256, 128, 32,
            4 * 1024 * 1024, 16 * 1024 * 1024, TimeSpan.FromSeconds(10));
        return BaseStudioBootstrapSnapshot.Create(invocation.ApplicationGraph.ApplicationId, _options.Mode,
            response, visibleModules.OrderBy(static value => value.ModuleId, StringComparer.Ordinal).ThenBy(static value => value.Version),
            visiblePages.OrderBy(static value => (byte)value.Area).ThenBy(static value => value.ModuleId, StringComparer.Ordinal).ThenBy(static value => value.PageId, StringComparer.Ordinal),
            visibleCommands.OrderBy(static value => value.ModuleId, StringComparer.Ordinal).ThenBy(static value => value.CommandId, StringComparer.Ordinal), visibleResolvers.OrderBy(static value => value.ModuleId, StringComparer.Ordinal).ThenBy(static value => (byte)value.Kind).ThenBy(static value => value.ResolverId, StringComparer.Ordinal), [],
            visibleClients.OrderBy(static value => value.ModuleId, StringComparer.Ordinal).ThenBy(static value => value.ClientId, StringComparer.Ordinal), map, limits, now,
            response.AuthorizedThroughUtc);
    }

    private static void ValidateGridPropertyCorrespondence(IEnumerable<BaseStudioVisiblePage> pages,
        IEnumerable<BaseStudioNamedTypeContract> types)
    {
        Dictionary<string, BaseStudioNamedTypeContract> nodes = types.ToDictionary(static value => value.TypeId, StringComparer.Ordinal);
        foreach (BaseStudioVisibleView view in pages.SelectMany(static page => page.Views))
        {
            BaseStudioGridDefinition? grid = view.Presentation.Grid; if (grid is null) continue;
            if (!StringComparer.Ordinal.Equals(grid.RowNodeId, view.ItemNodeId) ||
                !nodes.TryGetValue(grid.RowNodeId, out BaseStudioNamedTypeContract? node) ||
                !BaseStudioSha256.FixedTimeEquals(node.NodeChecksum, grid.RowNodeChecksum) ||
                !BaseStudioSha256.FixedTimeEquals(node.NodeChecksum, view.ItemNodeChecksum))
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

    private async ValueTask<bool> AdmittedAsync(BaseStudioBootstrapInvocation invocation,
        IEnumerable<BaseStudioGrantRequirement> grants, CancellationToken cancellationToken)
    {
        foreach (BaseStudioGrantRequirement grant in grants)
            if (await _authorization.AdmitAsync(invocation, grant, cancellationToken).ConfigureAwait(false) is null) return false;
        return true;
    }

    private async ValueTask<List<BaseStudioPageRegistration>> FilterPagesAsync(BaseStudioBootstrapInvocation invocation,
        IEnumerable<BaseStudioPageRegistration> pages, CancellationToken cancellationToken)
    {
        var result = new List<BaseStudioPageRegistration>();
        foreach (BaseStudioPageRegistration page in pages)
            if (await AdmittedAsync(invocation, page.Grants, cancellationToken).ConfigureAwait(false)) result.Add(page);
        return result;
    }

    internal static BaseStudioDynamicStoreAuthorityRequest StoreAuthorityRequest(string applicationId) => new()
    { ApplicationId = applicationId, MaximumEvidenceBytes = 4_096, MaximumTransientBytes = 16_384, Deadline = TimeSpan.FromSeconds(2) };

    internal static async ValueTask<OperationResult<BaseStudioDynamicStoreAuthority>?> CaptureStoreAsync(
        IBaseStudioDynamicStoreAuthoritySource source, BaseStudioLateWorkRegistry lateWork,
        BaseStudioDynamicStoreAuthorityRequest request, CancellationToken cancellationToken)
    {
        if (!lateWork.TryEnter(out BaseStudioLateWorkLease lease)) return null;
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken); deadline.CancelAfter(request.Deadline);
        Task<OperationResult<BaseStudioDynamicStoreAuthority>> task;
        try { task = source.CaptureStudioDynamicStoreAuthorityAsync(request, deadline.Token).AsTask(); }
        catch { lease.Dispose(); throw; }
        try { OperationResult<BaseStudioDynamicStoreAuthority> result = await task.WaitAsync(deadline.Token).ConfigureAwait(false); lease.Dispose(); return result; }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { lease.Retain(task); return null; }
        catch (OperationCanceledException) { lease.Retain(task); throw; }
        catch { lease.Dispose(); throw; }
    }

}
