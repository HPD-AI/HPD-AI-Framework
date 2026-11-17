using System.Collections.Immutable;

namespace HPD.AI.Platform.Studio;

/// <summary>Identifies a Studio page's semantic rendering class.</summary>
public enum StudioPageKind : byte { Overview = 1, Collection, ResourceList, ResourceDetail, Timeline, QueryTool, Action, Diagnostics }
/// <summary>Identifies the maximum disclosure admitted by a graph registration.</summary>
public enum StudioDisclosureClass : byte { PublicSafe = 1, AuthorizedMetadata, ProtectedValue, ConfidentialValue }
/// <summary>Identifies whether a framework client is mandatory for module activation.</summary>
public enum StudioContractNecessity : byte { Required = 1, Optional }
/// <summary>Identifies the only client protocols the Studio shell can instantiate.</summary>
public enum StudioFrameworkClientProtocol : byte { BaseL41DynamicMap = 1, FrameworkGeneratedContractV1 }
/// <summary>Identifies the transport authority admitted to a generated Studio client.</summary>
public enum StudioFrameworkClientTransportClass : byte { SameOriginShellAuthenticated = 1 }
/// <summary>Identifies navigation ownership derived from the reserved module identity.</summary>
public enum StudioModuleClass : byte { Base = 1, Framework }
/// <summary>Identifies a Studio command's minimum interaction ceremony.</summary>
public enum StudioActionClass : byte { Routine = 1, OperationalTransition, Maintenance, Destructive, DisasterOrRecoveryDomain }
/// <summary>Identifies one typed relation between Studio resources.</summary>
public enum StudioLinkRelation : byte
{
    Owns = 1, ContainedBy, Affected, ProducedBy, ReceiptFor, ScheduledBy, OccurrenceOf,
    AttemptOf, ChildOf, References, LifecycleOf, Blocks, AcknowledgedBy, IndexedBy,
    StoredBy, AuthorizedBy, Diagnoses, Remediates,
}
/// <summary>Identifies canonical Studio view ordering direction.</summary>
public enum StudioOrderDirection : byte { Ascending = 1, Descending }
/// <summary>Identifies canonical placement of missing and null values.</summary>
public enum StudioNullPlacement : byte { MissingThenNull = 1, NullThenMissing, ValuesThenMissingThenNull, ValuesThenNullThenMissing }

/// <summary>Defines one canonical ordering member.</summary>
public sealed class StudioOrderMember
{
    private StudioOrderMember(string property, StudioOrderDirection direction, StudioNullPlacement nulls, StudioSha256 checksum)
    { StablePropertyId = property; Direction = direction; NullPlacement = nulls; Checksum = checksum; }
    /// <summary>Gets the stable property identity.</summary>
    public string StablePropertyId { get; }
    /// <summary>Gets ordering direction.</summary>
    public StudioOrderDirection Direction { get; }
    /// <summary>Gets missing/null placement.</summary>
    public StudioNullPlacement NullPlacement { get; }
    /// <summary>Gets the canonical checksum.</summary>
    public StudioSha256 Checksum { get; }
    /// <summary>Creates one canonical ordering member.</summary>
    public static StudioOrderMember Create(string property, StudioOrderDirection direction, StudioNullPlacement nulls)
    {
        StudioContractValidation.Id(property); StudioContractValidation.Enum(direction); StudioContractValidation.Enum(nulls);
        StudioSha256 checksum = StudioCanonicalEncoding.Hash("base.studio.order-member.v1", writer =>
        { writer.String(property); writer.Enum(direction); writer.Enum(nulls); });
        return new(property, direction, nulls, checksum);
    }
}

/// <summary>Defines one closed generated filter variant.</summary>
public sealed class StudioFilterRegistration
{
    private StudioFilterRegistration(string id, string node, StudioSha256 nodeChecksum, StudioSha256 checksum)
    { FilterId = id; RequestNodeId = node; RequestNodeChecksum = nodeChecksum; Checksum = checksum; }
    /// <summary>Gets the filter identity.</summary>
    public string FilterId { get; }
    /// <summary>Gets the exact L41 request-node identity.</summary>
    public string RequestNodeId { get; }
    /// <summary>Gets the request-node checksum.</summary>
    public StudioSha256 RequestNodeChecksum { get; }
    /// <summary>Gets the canonical checksum.</summary>
    public StudioSha256 Checksum { get; }
    /// <summary>Creates one exact filter variant.</summary>
    public static StudioFilterRegistration Create(string id, string node, StudioSha256 nodeChecksum)
    {
        StudioContractValidation.Id(id); StudioContractValidation.Id(node); ArgumentNullException.ThrowIfNull(nodeChecksum);
        StudioSha256 owned = StudioSha256.FromBytes(nodeChecksum.ToArray());
        StudioSha256 checksum = StudioCanonicalEncoding.Hash("base.studio.filter.v1", writer =>
        { writer.String(id); writer.String(node); writer.Checksum(owned); });
        return new(id, node, owned, checksum);
    }
}

/// <summary>Defines one closed generated sort variant.</summary>
public sealed class StudioSortRegistration
{
    private StudioSortRegistration(string id, ImmutableArray<StudioOrderMember> members, StudioSha256 checksum)
    { SortId = id; Members = members; Checksum = checksum; }
    /// <summary>Gets the sort identity.</summary>
    public string SortId { get; }
    /// <summary>Gets its complete canonical ordering tuple.</summary>
    public ImmutableArray<StudioOrderMember> Members { get; }
    /// <summary>Gets the canonical checksum.</summary>
    public StudioSha256 Checksum { get; }
    /// <summary>Creates one exact sort variant.</summary>
    public static StudioSortRegistration Create(string id, IEnumerable<StudioOrderMember> members)
    {
        StudioContractValidation.Id(id);
        ImmutableArray<StudioOrderMember> owned = StudioContractValidation.Materialize(members, 16, false, nameof(members));
        if (owned.Select(static value => value.StablePropertyId).Distinct(StringComparer.Ordinal).Count() != owned.Length)
            throw new ArgumentException("A Studio sort repeats an ordering property.", nameof(members));
        StudioSha256 checksum = StudioCanonicalEncoding.Hash("base.studio.sort.v1", writer =>
        { writer.String(id); writer.Count(owned.Length); foreach (StudioOrderMember value in owned) writer.Checksum(value.Checksum); });
        return new(id, owned, checksum);
    }
}

/// <summary>Defines the protected-scope rule carried by an installed Studio grant requirement.</summary>
public enum StudioProtectedScopeRule : byte
{
    /// <summary>The grant is application scoped.</summary>
    Application = 1,
    /// <summary>The grant requires an exact tenant scope.</summary>
    Tenant,
    /// <summary>The grant requires an exact project scope.</summary>
    Project,
    /// <summary>The grant requires the exact installed tenant/project scope.</summary>
    TenantProject,
    /// <summary>The grant uses the exact resource-owned protected scope.</summary>
    ResourceExact,
}

/// <summary>Defines one exact installed policy grant required for discovery or execution.</summary>
public sealed class StudioGrantRequirement
{
    private StudioGrantRequirement(string id, int version, StudioSha256 registration, string operation,
        string audience, string subject, string application, string module, StudioResourceKind? resource,
        StudioProtectedScopeRule scope, bool underlying, StudioSha256 checksum)
    { GrantId = id; Version = version; RegistrationChecksum = registration; OperationId = operation; Audience = audience;
      SubjectKind = subject; ApplicationId = application; OwningModuleId = module; ResourceKind = resource;
      ScopeRule = scope; RequiresUnderlyingOperationGrant = underlying; Checksum = checksum; }
    /// <summary>Gets the grant identity.</summary>
    public string GrantId { get; }
    /// <summary>Gets the grant version.</summary>
    public int Version { get; }
    /// <summary>Gets the exact installed L38 grant-registration checksum.</summary>
    public StudioSha256 RegistrationChecksum { get; }
    /// <summary>Gets the fixed Studio operation identity.</summary>
    public string OperationId { get; }
    /// <summary>Gets the exact ControlPlane audience identity.</summary>
    public string Audience { get; }
    /// <summary>Gets the admitted subject-kind identity.</summary>
    public string SubjectKind { get; }
    /// <summary>Gets the owning application identity.</summary>
    public string ApplicationId { get; }
    /// <summary>Gets the owning framework module identity.</summary>
    public string OwningModuleId { get; }
    /// <summary>Gets the resource kind when this requirement is resource-specific.</summary>
    public StudioResourceKind? ResourceKind { get; }
    /// <summary>Gets the exact protected-scope composition rule.</summary>
    public StudioProtectedScopeRule ScopeRule { get; }
    /// <summary>Gets whether the underlying subsystem operation grant must also be admitted.</summary>
    public bool RequiresUnderlyingOperationGrant { get; }
    /// <summary>Gets the canonical checksum.</summary>
    public StudioSha256 Checksum { get; }
    /// <summary>Creates one exact grant requirement.</summary>
    public static StudioGrantRequirement Create(string id, int version, StudioSha256 registrationChecksum,
        string operationId, string audience, string subjectKind, string applicationId, string owningModuleId,
        StudioResourceKind? resourceKind, StudioProtectedScopeRule scopeRule, bool requiresUnderlyingOperationGrant)
    {
        StudioContractValidation.Id(id); StudioContractValidation.Id(operationId); StudioContractValidation.Id(audience);
        StudioContractValidation.Id(subjectKind); StudioContractValidation.Id(applicationId); StudioContractValidation.Id(owningModuleId);
        ArgumentNullException.ThrowIfNull(registrationChecksum); StudioContractValidation.Enum(scopeRule);
        if (version < 1 || resourceKind is { } kind && !Enum.IsDefined(kind)) throw new ArgumentOutOfRangeException(nameof(version));
        StudioSha256 registration = StudioSha256.FromBytes(registrationChecksum.ToArray());
        StudioSha256 checksum = StudioCanonicalEncoding.Hash("base.studio.grant.v1", writer =>
        { writer.String(id); writer.Int32(version); writer.Checksum(registration); writer.String(operationId); writer.String(audience);
          writer.String(subjectKind); writer.String(applicationId); writer.String(owningModuleId); writer.Boolean(resourceKind.HasValue);
          if (resourceKind.HasValue) writer.Enum(resourceKind.Value); writer.Enum(scopeRule); writer.Boolean(requiresUnderlyingOperationGrant); });
        return new(id, version, registration, operationId, audience, subjectKind, applicationId, owningModuleId,
            resourceKind, scopeRule, requiresUnderlyingOperationGrant, checksum);
    }
}

/// <summary>Defines one generated framework-client slot.</summary>
public sealed class StudioFrameworkClientRegistration
{
    private StudioFrameworkClientRegistration(string id, int version, StudioContractNecessity necessity,
        StudioFrameworkClientProtocol protocol, StudioSha256 runtimeAbi, StudioSha256 contract,
        StudioSha256 operations, string endpointSurface, StudioFrameworkClientTransportClass transport,
        ImmutableArray<string> pages, StudioFrameworkClientLimits limits, StudioSha256 checksum)
    { ClientId = id; Version = version; Necessity = necessity; Protocol = protocol; StaticRuntimeAbiChecksum = runtimeAbi;
      GeneratedContractChecksum = contract; OperationInventoryChecksum = operations; EndpointSurfaceId = endpointSurface;
      TransportClass = transport; OwningPageIds = pages; Limits = limits; Checksum = checksum; }
    /// <summary>Gets the client identity.</summary>
    public string ClientId { get; }
    /// <summary>Gets the client version.</summary>
    public int Version { get; }
    /// <summary>Gets whether the client is required.</summary>
    public StudioContractNecessity Necessity { get; }
    /// <summary>Gets the closed generated-client protocol.</summary>
    public StudioFrameworkClientProtocol Protocol { get; }
    /// <summary>Gets the static runtime ABI checksum.</summary>
    public StudioSha256 StaticRuntimeAbiChecksum { get; }
    /// <summary>Gets the installed graph contract checksum.</summary>
    public StudioSha256 GeneratedContractChecksum { get; }
    /// <summary>Gets the exact generated operation-inventory checksum.</summary>
    public StudioSha256 OperationInventoryChecksum { get; }
    /// <summary>Gets the server-installed endpoint surface identity.</summary>
    public string EndpointSurfaceId { get; }
    /// <summary>Gets the only admitted browser transport class.</summary>
    public StudioFrameworkClientTransportClass TransportClass { get; }
    /// <summary>Gets the pages that are permitted to consume this client.</summary>
    public ImmutableArray<string> OwningPageIds { get; }
    /// <summary>Gets the exact bounded client authority.</summary>
    public StudioFrameworkClientLimits Limits { get; }
    /// <summary>Gets the registration checksum.</summary>
    public StudioSha256 Checksum { get; }
    /// <summary>Creates one generated framework-client registration.</summary>
    public static StudioFrameworkClientRegistration Create(string id, int version, StudioContractNecessity necessity,
        StudioFrameworkClientProtocol protocol, StudioSha256 runtimeAbi, StudioSha256 contract,
        StudioSha256 operations, string endpointSurface, StudioFrameworkClientTransportClass transport,
        IEnumerable<string> owningPages, StudioFrameworkClientLimits limits)
    {
        StudioContractValidation.Id(id); StudioContractValidation.Enum(necessity); StudioContractValidation.Enum(protocol);
        StudioContractValidation.Id(endpointSurface); StudioContractValidation.Enum(transport);
        if (version < 1) throw new ArgumentOutOfRangeException(nameof(version));
        ArgumentNullException.ThrowIfNull(runtimeAbi); ArgumentNullException.ThrowIfNull(contract);
        ArgumentNullException.ThrowIfNull(operations); ArgumentNullException.ThrowIfNull(limits);
        StudioSha256 ownedAbi = StudioSha256.FromBytes(runtimeAbi.ToArray());
        StudioSha256 ownedContract = StudioSha256.FromBytes(contract.ToArray());
        StudioSha256 ownedOperations = StudioSha256.FromBytes(operations.ToArray());
        ImmutableArray<string> pages = StudioContractValidation.Ids(owningPages, 64, false, nameof(owningPages));
        StudioSha256 checksum = StudioCanonicalEncoding.Hash("base.studio.frameworkClient.v1", writer =>
        { writer.String(id); writer.Int32(version); writer.Enum(necessity); writer.Enum(protocol); writer.Checksum(ownedAbi);
          writer.Checksum(ownedContract); writer.Checksum(ownedOperations); writer.String(endpointSurface); writer.Enum(transport);
          writer.Count(pages.Length); foreach (string page in pages) writer.String(page); writer.Checksum(limits.Checksum); });
        return new(id, version, necessity, protocol, ownedAbi, ownedContract, ownedOperations, endpointSurface,
            transport, pages, limits, checksum);
    }
}

/// <summary>Defines the bounded authority supplied to one shell-owned framework client.</summary>
public sealed class StudioFrameworkClientLimits
{
    private StudioFrameworkClientLimits(int operations, long requestBytes, long responseBytes, int concurrent,
        TimeSpan acquisition, TimeSpan operation, TimeSpan disposal, StudioSha256 checksum)
    { MaximumOperations = operations; MaximumRequestBytes = requestBytes; MaximumResponseBytes = responseBytes;
      MaximumConcurrentRequests = concurrent; AcquisitionDeadline = acquisition; OperationDeadline = operation;
      DisposalDeadline = disposal; Checksum = checksum; }
    /// <summary>Gets the maximum registered operations.</summary>
    public int MaximumOperations { get; }
    /// <summary>Gets the maximum canonical request bytes.</summary>
    public long MaximumRequestBytes { get; }
    /// <summary>Gets the maximum response bytes.</summary>
    public long MaximumResponseBytes { get; }
    /// <summary>Gets the maximum concurrent requests.</summary>
    public int MaximumConcurrentRequests { get; }
    /// <summary>Gets the client-acquisition deadline.</summary>
    public TimeSpan AcquisitionDeadline { get; }
    /// <summary>Gets the per-operation deadline.</summary>
    public TimeSpan OperationDeadline { get; }
    /// <summary>Gets the client-disposal deadline.</summary>
    public TimeSpan DisposalDeadline { get; }
    /// <summary>Gets the canonical limits checksum.</summary>
    public StudioSha256 Checksum { get; }
    /// <summary>Creates one independently bounded framework-client limit envelope.</summary>
    public static StudioFrameworkClientLimits Create(int operations, long requestBytes, long responseBytes,
        int concurrent, TimeSpan acquisition, TimeSpan operation, TimeSpan disposal)
    {
        if (operations is < 1 or > 4096 || requestBytes is < 1 or > 67_108_864 || responseBytes is < 1 or > 67_108_864 ||
            concurrent is < 1 or > 256 || acquisition <= TimeSpan.Zero || acquisition > TimeSpan.FromSeconds(30) ||
            operation <= TimeSpan.Zero || operation > TimeSpan.FromMinutes(5) || disposal <= TimeSpan.Zero || disposal > TimeSpan.FromSeconds(30))
            throw new ArgumentOutOfRangeException(nameof(operations));
        StudioSha256 checksum = StudioCanonicalEncoding.Hash("base.studio.frameworkClient.limits.v1", writer =>
        { writer.Int32(operations); writer.Int64(requestBytes); writer.Int64(responseBytes); writer.Int32(concurrent);
          writer.Int64(checked((long)acquisition.TotalMilliseconds)); writer.Int64(checked((long)operation.TotalMilliseconds));
          writer.Int64(checked((long)disposal.TotalMilliseconds)); });
        return new(operations, requestBytes, responseBytes, concurrent, acquisition, operation, disposal, checksum);
    }
}

/// <summary>Defines one exact generated view contract.</summary>
public sealed class StudioViewRegistration
{
    private StudioViewRegistration(string id, int version, string producer, string requestNode, StudioSha256 requestChecksum,
        StudioResourceKind itemKind, string itemNode, StudioSha256 itemChecksum, string cursorPurpose, ImmutableArray<StudioOrderMember> canonicalOrder,
        ImmutableArray<StudioFilterRegistration> filters, ImmutableArray<StudioSortRegistration> sorts,
        StudioSha256 disclosureChecksum, long maximumBytes, int maximumItems,
        StudioViewPresentationRegistration presentation, StudioSha256 checksum)
    { ViewId = id; Version = version; ProducerId = producer; RequestNodeId = requestNode; RequestNodeChecksum = requestChecksum;
      ItemKind = itemKind; ItemNodeId = itemNode; ItemNodeChecksum = itemChecksum; CursorPurpose = cursorPurpose; CanonicalOrder = canonicalOrder;
      Filters = filters; Sorts = sorts; DisclosureChannelChecksum = disclosureChecksum; MaximumBytes = maximumBytes;
      MaximumItems = maximumItems; Presentation = presentation; Checksum = checksum; }
    /// <summary>Gets the view identity.</summary>
    public string ViewId { get; }
    /// <summary>Gets the view version.</summary>
    public int Version { get; }
    /// <summary>Gets the Runtime producer identity.</summary>
    public string ProducerId { get; }
    /// <summary>Gets the L41 request-node identity.</summary>
    public string RequestNodeId { get; }
    /// <summary>Gets the request-node checksum.</summary>
    public StudioSha256 RequestNodeChecksum { get; }
    /// <summary>Gets the returned resource kind.</summary>
    public StudioResourceKind ItemKind { get; }
    /// <summary>Gets the L41 item-node identity.</summary>
    public string ItemNodeId { get; }
    /// <summary>Gets the item-node checksum.</summary>
    public StudioSha256 ItemNodeChecksum { get; }
    /// <summary>Gets the protected cursor purpose.</summary>
    public string CursorPurpose { get; }
    /// <summary>Gets the mandatory canonical final ordering tuple.</summary>
    public ImmutableArray<StudioOrderMember> CanonicalOrder { get; }
    /// <summary>Gets closed generated filter variants.</summary>
    public ImmutableArray<StudioFilterRegistration> Filters { get; }
    /// <summary>Gets closed generated sort variants.</summary>
    public ImmutableArray<StudioSortRegistration> Sorts { get; }
    /// <summary>Gets the disclosure-channel checksum.</summary>
    public StudioSha256 DisclosureChannelChecksum { get; }
    /// <summary>Gets the maximum result bytes.</summary>
    public long MaximumBytes { get; }
    /// <summary>Gets the maximum result items.</summary>
    public int MaximumItems { get; }
    /// <summary>Gets immutable presentation mechanics.</summary>
    public StudioViewPresentationRegistration Presentation { get; }
    /// <summary>Gets the canonical checksum.</summary>
    public StudioSha256 Checksum { get; }

    /// <summary>Creates and checksums one generated view contract.</summary>
    public static StudioViewRegistration Create(string id, int version, string producer, string requestNode,
        StudioSha256 requestChecksum, StudioResourceKind itemKind, string itemNode, StudioSha256 itemChecksum, string cursorPurpose,
        IEnumerable<StudioOrderMember> canonicalOrder, IEnumerable<StudioFilterRegistration>? filters,
        IEnumerable<StudioSortRegistration>? sorts, StudioSha256 disclosureChecksum,
        long maximumBytes, int maximumItems,
        StudioViewPresentationRegistration presentation)
    {
        StudioContractValidation.Id(id); StudioContractValidation.Id(producer); StudioContractValidation.Id(requestNode);
        StudioContractValidation.Enum(itemKind); StudioContractValidation.Id(itemNode); StudioContractValidation.Id(cursorPurpose);
        if (version < 1 || maximumBytes < 1 || maximumItems < 1) throw new ArgumentOutOfRangeException(nameof(version));
        ArgumentNullException.ThrowIfNull(requestChecksum); ArgumentNullException.ThrowIfNull(itemChecksum);
        ArgumentNullException.ThrowIfNull(disclosureChecksum); ArgumentNullException.ThrowIfNull(presentation);
        if (!StringComparer.Ordinal.Equals(id, presentation.ViewId)) throw new ArgumentException("Studio view presentation identity differs from its view.", nameof(presentation));
        ImmutableArray<StudioOrderMember> ownedOrder = StudioContractValidation.Materialize(canonicalOrder, 16, false, nameof(canonicalOrder));
        if (ownedOrder.Select(static value => value.StablePropertyId).Distinct(StringComparer.Ordinal).Count() != ownedOrder.Length)
            throw new ArgumentException("Studio canonical order repeats a property.", nameof(canonicalOrder));
        ImmutableArray<StudioFilterRegistration> ownedFilters = StudioGraphValidation.OrderedIdentity(filters ?? [], 64, static value => value.FilterId, nameof(filters));
        ImmutableArray<StudioSortRegistration> ownedSorts = StudioGraphValidation.OrderedIdentity(sorts ?? [], 64, static value => value.SortId, nameof(sorts));
        if (presentation.Grid is not null && presentation.Grid.Columns.Any(column =>
                column.FilterId is not null && !ownedFilters.Any(value => StringComparer.Ordinal.Equals(value.FilterId, column.FilterId)) ||
                column.SortId is not null && !ownedSorts.Any(value => StringComparer.Ordinal.Equals(value.SortId, column.SortId))))
            throw new ArgumentException("Studio grid references an unregistered filter or sort.", nameof(presentation));
        StudioSha256 request = StudioSha256.FromBytes(requestChecksum.ToArray());
        StudioSha256 item = StudioSha256.FromBytes(itemChecksum.ToArray());
        StudioSha256 disclosure = StudioSha256.FromBytes(disclosureChecksum.ToArray());
        StudioSha256 checksum = StudioCanonicalEncoding.Hash("base.studio.view.v1", writer =>
        {
            writer.String(id); writer.Int32(version); writer.String(producer); writer.String(requestNode); writer.Checksum(request);
            writer.Enum(itemKind); writer.String(itemNode); writer.Checksum(item); writer.String(cursorPurpose);
            StudioGraphValidation.Encode(writer, ownedOrder, static value => value.Checksum);
            StudioGraphValidation.Encode(writer, ownedFilters, static value => value.Checksum);
            StudioGraphValidation.Encode(writer, ownedSorts, static value => value.Checksum); writer.Checksum(disclosure);
            writer.Int64(maximumBytes); writer.Int32(maximumItems); writer.Checksum(presentation.Checksum);
        });
        return new(id, version, producer, requestNode, request, itemKind, itemNode, item, cursorPurpose, ownedOrder,
            ownedFilters, ownedSorts, disclosure, maximumBytes, maximumItems, presentation, checksum);
    }
}

/// <summary>Defines one exact Studio page.</summary>
public sealed class StudioPageRegistration
{
    private StudioPageRegistration(string id, int version, StudioArea area, string label,
        StudioRouteTemplate route, StudioPageKind kind, StudioPagePresentationRegistration presentation,
        ImmutableArray<StudioResourceKind> resources, ImmutableArray<string> endpoints,
        ImmutableArray<StudioGrantRequirement> grants, StudioDisclosureClass disclosure, StudioSha256 checksum)
    { PageId = id; Version = version; Area = area; LabelMessageId = label; Route = route; Kind = kind; Presentation = presentation;
      AcceptedResources = resources; RequiredEndpointIds = endpoints; Grants = grants; Disclosure = disclosure; Checksum = checksum; }
    /// <summary>Gets the page identity.</summary>
    public string PageId { get; }
    /// <summary>Gets the page version.</summary>
    public int Version { get; }
    /// <summary>Gets the task area.</summary>
    public StudioArea Area { get; }
    /// <summary>Gets the localized page-label identity.</summary>
    public string LabelMessageId { get; }
    /// <summary>Gets the route.</summary>
    public StudioRouteTemplate Route { get; }
    /// <summary>Gets the semantic page kind.</summary>
    public StudioPageKind Kind { get; }
    /// <summary>Gets presentation mechanics.</summary>
    public StudioPagePresentationRegistration Presentation { get; }
    /// <summary>Gets accepted resource kinds.</summary>
    public ImmutableArray<StudioResourceKind> AcceptedResources { get; }
    /// <summary>Gets exact required endpoint identities.</summary>
    public ImmutableArray<string> RequiredEndpointIds { get; }
    /// <summary>Gets discovery grants.</summary>
    public ImmutableArray<StudioGrantRequirement> Grants { get; }
    /// <summary>Gets maximum disclosure.</summary>
    public StudioDisclosureClass Disclosure { get; }
    /// <summary>Gets the canonical checksum.</summary>
    public StudioSha256 Checksum { get; }

    /// <summary>Creates and checksums one exact page registration.</summary>
    public static StudioPageRegistration Create(string id, int version, StudioArea area, string label,
        StudioRouteTemplate route, StudioPageKind kind, StudioPagePresentationRegistration presentation,
        IEnumerable<StudioResourceKind> acceptedResources, IEnumerable<string> endpointIds,
        IEnumerable<StudioGrantRequirement> grants, StudioDisclosureClass disclosure)
    {
        StudioContractValidation.Id(id); StudioContractValidation.Id(label); StudioContractValidation.Enum(area);
        StudioContractValidation.Enum(kind); StudioContractValidation.Enum(disclosure);
        ArgumentNullException.ThrowIfNull(route); ArgumentNullException.ThrowIfNull(presentation);
        if (version < 1) throw new ArgumentOutOfRangeException(nameof(version));
        if (!StringComparer.Ordinal.Equals(id, presentation.PageId) || version != presentation.PageVersion)
            throw new ArgumentException("Studio page presentation owner differs from its page.", nameof(presentation));
        ImmutableArray<StudioResourceKind> resources = StudioContractValidation.Materialize(acceptedResources, 64, true, nameof(acceptedResources));
        if (resources.Any(static value => !Enum.IsDefined(value)) || resources.Distinct().Count() != resources.Length ||
            !resources.SequenceEqual(resources.OrderBy(static value => (byte)value))) throw new ArgumentException("Studio resource kinds are not canonical.", nameof(acceptedResources));
        ImmutableArray<string> endpoints = StudioContractValidation.Ids(endpointIds, 128, true, nameof(endpointIds));
        ImmutableArray<StudioGrantRequirement> ownedGrants = StudioGraphValidation.Grants(grants, nameof(grants));
        StudioSha256 checksum = StudioCanonicalEncoding.Hash("base.studio.page.v1", writer =>
        {
            writer.String(id); writer.Int32(version); writer.Enum(area); writer.String(label); writer.Checksum(route.Checksum);
            writer.Enum(kind); writer.Checksum(presentation.Checksum); writer.Count(resources.Length); foreach (StudioResourceKind value in resources) writer.Enum(value);
            writer.Count(endpoints.Length); foreach (string value in endpoints) writer.String(value);
            writer.Count(ownedGrants.Length); foreach (StudioGrantRequirement value in ownedGrants) writer.Checksum(value.Checksum); writer.Enum(disclosure);
        });
        return new(id, version, area, label, route, kind, presentation, resources, endpoints, ownedGrants, disclosure, checksum);
    }
}

/// <summary>Defines one graph-owned resource resolver.</summary>
public sealed class StudioResourceRegistration
{
    private StudioResourceRegistration(StudioResourceKind kind, string resolver, ImmutableArray<string> endpoints,
        ImmutableArray<StudioGrantRequirement> grants, StudioDisclosureClass disclosure, StudioSha256 checksum)
    { Kind = kind; ResolverId = resolver; EndpointIds = endpoints; Grants = grants; Disclosure = disclosure; Checksum = checksum; }
    /// <summary>Gets the resource kind.</summary>
    public StudioResourceKind Kind { get; }
    /// <summary>Gets the Runtime resolver identity.</summary>
    public string ResolverId { get; }
    /// <summary>Gets resolver endpoint identities.</summary>
    public ImmutableArray<string> EndpointIds { get; }
    /// <summary>Gets discovery grants.</summary>
    public ImmutableArray<StudioGrantRequirement> Grants { get; }
    /// <summary>Gets maximum disclosure.</summary>
    public StudioDisclosureClass Disclosure { get; }
    /// <summary>Gets the canonical checksum.</summary>
    public StudioSha256 Checksum { get; }
    /// <summary>Creates and checksums one resource resolver.</summary>
    public static StudioResourceRegistration Create(StudioResourceKind kind, string resolver,
        IEnumerable<string> endpoints, IEnumerable<StudioGrantRequirement> grants, StudioDisclosureClass disclosure)
    {
        StudioContractValidation.Enum(kind); StudioContractValidation.Id(resolver); StudioContractValidation.Enum(disclosure);
        ImmutableArray<string> ownedEndpoints = StudioContractValidation.Ids(endpoints, 128, false, nameof(endpoints));
        ImmutableArray<StudioGrantRequirement> ownedGrants = StudioGraphValidation.Grants(grants, nameof(grants));
        StudioSha256 checksum = StudioCanonicalEncoding.Hash("base.studio.resource.v1", writer =>
        { writer.Enum(kind); writer.String(resolver); writer.Count(ownedEndpoints.Length); foreach (string value in ownedEndpoints) writer.String(value);
          writer.Count(ownedGrants.Length); foreach (StudioGrantRequirement value in ownedGrants) writer.Checksum(value.Checksum); writer.Enum(disclosure); });
        return new(kind, resolver, ownedEndpoints, ownedGrants, disclosure, checksum);
    }
}

/// <summary>Defines one graph-owned, generated Studio command.</summary>
public sealed class StudioCommandRegistration
{
    private StudioCommandRegistration(string id, int version, string operation, string inputNode, StudioSha256 inputChecksum,
        string resultNode, StudioSha256 resultChecksum, StudioActionClass action, ImmutableArray<StudioGrantRequirement> grants,
        long maximumRequestBytes, long maximumResultBytes, StudioFreshAuthenticationClass? freshAuthentication,
        ImmutableArray<StudioCommandAcknowledgementRequirement> acknowledgements, StudioSha256 checksum)
    { CommandId = id; Version = version; OperationId = operation; InputNodeId = inputNode; InputNodeChecksum = inputChecksum;
      ResultNodeId = resultNode; ResultNodeChecksum = resultChecksum; ActionClass = action; Grants = grants;
      MaximumRequestBytes = maximumRequestBytes; MaximumResultBytes = maximumResultBytes; FreshAuthentication = freshAuthentication;
      Acknowledgements = acknowledgements; Checksum = checksum; }
    /// <summary>Gets the command identity.</summary>
    public string CommandId { get; }
    /// <summary>Gets the command version.</summary>
    public int Version { get; }
    /// <summary>Gets the underlying Runtime operation identity.</summary>
    public string OperationId { get; }
    /// <summary>Gets the L41 input-node identity.</summary>
    public string InputNodeId { get; }
    /// <summary>Gets the input-node checksum.</summary>
    public StudioSha256 InputNodeChecksum { get; }
    /// <summary>Gets the L41 result-node identity.</summary>
    public string ResultNodeId { get; }
    /// <summary>Gets the result-node checksum.</summary>
    public StudioSha256 ResultNodeChecksum { get; }
    /// <summary>Gets the minimum interaction ceremony.</summary>
    public StudioActionClass ActionClass { get; }
    /// <summary>Gets execution grants.</summary>
    public ImmutableArray<StudioGrantRequirement> Grants { get; }
    /// <summary>Gets the maximum request bytes.</summary>
    public long MaximumRequestBytes { get; }
    /// <summary>Gets the maximum result bytes.</summary>
    public long MaximumResultBytes { get; }
    /// <summary>Gets the required fresh-authentication class, or null when none is required.</summary>
    public StudioFreshAuthenticationClass? FreshAuthentication { get; }
    /// <summary>Gets the exact acknowledgement evidence required by execution.</summary>
    public ImmutableArray<StudioCommandAcknowledgementRequirement> Acknowledgements { get; }
    /// <summary>Gets the canonical checksum.</summary>
    public StudioSha256 Checksum { get; }
    /// <summary>Creates and checksums one exact command.</summary>
    public static StudioCommandRegistration Create(string id, int version, string operation, string inputNode,
        StudioSha256 inputChecksum, string resultNode, StudioSha256 resultChecksum, StudioActionClass action,
        IEnumerable<StudioGrantRequirement> grants, long maximumRequestBytes, long maximumResultBytes,
        StudioFreshAuthenticationClass? freshAuthentication, IEnumerable<StudioCommandAcknowledgementRequirement> acknowledgements)
    {
        StudioContractValidation.Id(id); StudioContractValidation.Id(operation); StudioContractValidation.Id(inputNode); StudioContractValidation.Id(resultNode);
        StudioContractValidation.Enum(action); ArgumentNullException.ThrowIfNull(inputChecksum); ArgumentNullException.ThrowIfNull(resultChecksum);
        if (version < 1 || maximumRequestBytes < 1 || maximumResultBytes < 1) throw new ArgumentOutOfRangeException(nameof(version));
        ImmutableArray<StudioGrantRequirement> ownedGrants = StudioGraphValidation.Grants(grants, nameof(grants));
        if (freshAuthentication is { } authentication) StudioContractValidation.Enum(authentication);
        ImmutableArray<StudioCommandAcknowledgementRequirement> ownedAcknowledgements = StudioGraphValidation.OrderedIdentity(
            acknowledgements, 32, static value => value.PurposeId, nameof(acknowledgements));
        StudioSha256 input = StudioSha256.FromBytes(inputChecksum.ToArray()); StudioSha256 result = StudioSha256.FromBytes(resultChecksum.ToArray());
        StudioSha256 checksum = StudioCanonicalEncoding.Hash("base.studio.command.v1", writer =>
        { writer.String(id); writer.Int32(version); writer.String(operation); writer.String(inputNode); writer.Checksum(input);
          writer.String(resultNode); writer.Checksum(result); writer.Enum(action); writer.Count(ownedGrants.Length);
          foreach (StudioGrantRequirement value in ownedGrants) writer.Checksum(value.Checksum); writer.Int64(maximumRequestBytes); writer.Int64(maximumResultBytes);
          writer.Boolean(freshAuthentication.HasValue); if (freshAuthentication.HasValue) writer.Enum(freshAuthentication.Value);
          writer.Count(ownedAcknowledgements.Length); foreach (var value in ownedAcknowledgements) writer.Checksum(value.Checksum); });
        return new(id, version, operation, inputNode, input, resultNode, result, action, ownedGrants, maximumRequestBytes, maximumResultBytes,
            freshAuthentication, ownedAcknowledgements, checksum);
    }
}

/// <summary>Defines one exact purpose-and-impact acknowledgement required by a reviewed command.</summary>
public sealed class StudioCommandAcknowledgementRequirement
{
    private StudioCommandAcknowledgementRequirement(string purposeId, string impactId, StudioSha256 checksum)
    { PurposeId = purposeId; ImpactId = impactId; Checksum = checksum; }
    /// <summary>Gets the stable acknowledgement purpose.</summary>
    public string PurposeId { get; }
    /// <summary>Gets the stable semantic impact.</summary>
    public string ImpactId { get; }
    /// <summary>Gets the purpose-bound requirement checksum.</summary>
    public StudioSha256 Checksum { get; }
    /// <summary>Creates one exact acknowledgement requirement.</summary>
    public static StudioCommandAcknowledgementRequirement Create(string purposeId, string impactId)
    {
        StudioContractValidation.Id(purposeId); StudioContractValidation.Id(impactId);
        return new(new(purposeId.AsSpan()), new(impactId.AsSpan()), StudioCanonicalEncoding.Hash("base.studio.command-acknowledgement.v1", w => { w.String(purposeId); w.String(impactId); }));
    }
}

/// <summary>Defines one permitted typed relation between resource kinds.</summary>
public sealed class StudioLinkRegistration
{
    private StudioLinkRegistration(StudioResourceKind source, StudioResourceKind target,
        StudioLinkRelation relation, string resolver, StudioSha256 checksum)
    { SourceKind = source; TargetKind = target; Relation = relation; ResolverId = resolver; Checksum = checksum; }
    /// <summary>Gets the source kind.</summary>
    public StudioResourceKind SourceKind { get; }
    /// <summary>Gets the target kind.</summary>
    public StudioResourceKind TargetKind { get; }
    /// <summary>Gets the semantic relation.</summary>
    public StudioLinkRelation Relation { get; }
    /// <summary>Gets the Runtime resolver identity.</summary>
    public string ResolverId { get; }
    /// <summary>Gets the canonical checksum.</summary>
    public StudioSha256 Checksum { get; }
    /// <summary>Creates one typed link registration.</summary>
    public static StudioLinkRegistration Create(StudioResourceKind source, StudioResourceKind target,
        StudioLinkRelation relation, string resolver)
    {
        StudioContractValidation.Enum(source); StudioContractValidation.Enum(target); StudioContractValidation.Enum(relation); StudioContractValidation.Id(resolver);
        StudioSha256 checksum = StudioCanonicalEncoding.Hash("base.studio.link.v1", writer =>
        { writer.Enum(source); writer.Enum(target); writer.Enum(relation); writer.String(resolver); });
        return new(source, target, relation, resolver, checksum);
    }
}

/// <summary>Defines bounded module-owned Studio graph capacities.</summary>
public sealed class StudioModuleLimits
{
    private StudioModuleLimits(int pages, int views, int resources, int commands, int links, int clients, StudioSha256 checksum)
    { MaximumPages = pages; MaximumViews = views; MaximumResources = resources; MaximumCommands = commands; MaximumLinks = links; MaximumClients = clients; Checksum = checksum; }
    /// <summary>Gets the maximum page count.</summary>
    public int MaximumPages { get; }
    /// <summary>Gets the maximum view count.</summary>
    public int MaximumViews { get; }
    /// <summary>Gets the maximum resource count.</summary>
    public int MaximumResources { get; }
    /// <summary>Gets the maximum command count.</summary>
    public int MaximumCommands { get; }
    /// <summary>Gets the maximum link count.</summary>
    public int MaximumLinks { get; }
    /// <summary>Gets the maximum client count.</summary>
    public int MaximumClients { get; }
    /// <summary>Gets the canonical checksum.</summary>
    public StudioSha256 Checksum { get; }
    /// <summary>Creates module capacity limits.</summary>
    public static StudioModuleLimits Create(int pages, int views, int resources, int commands, int links, int clients)
    {
        if (pages is < 1 or > 64 || views is < 1 or > 256 || resources is < 1 or > 128 || commands is < 0 or > 256 || links is < 0 or > 256 || clients is < 1 or > 32)
            throw new ArgumentOutOfRangeException(nameof(pages));
        StudioSha256 checksum = StudioCanonicalEncoding.Hash("base.studio.module-limits.v1", writer =>
        { writer.Int32(pages); writer.Int32(views); writer.Int32(resources); writer.Int32(commands); writer.Int32(links); writer.Int32(clients); });
        return new(pages, views, resources, commands, links, clients, checksum);
    }
}

/// <summary>Identifies one frozen Studio module graph.</summary>
public sealed class StudioModuleIdentity
{
    internal StudioModuleIdentity(string id, int version, StudioSha256 checksum)
    { ModuleId = id; Version = version; Checksum = checksum; }
    /// <summary>Gets the module identity.</summary>
    public string ModuleId { get; }
    /// <summary>Gets the module version.</summary>
    public int Version { get; }
    /// <summary>Gets the application-specific registration checksum.</summary>
    public StudioSha256 Checksum { get; }
}

/// <summary>Represents one immutable, application-owned Studio module graph.</summary>
public sealed class StudioModuleRegistration
{
    private StudioModuleRegistration(StudioModuleIdentity identity, string application, string owner, string display,
        StudioModuleClass moduleClass, StudioAssetManifest asset, StudioFrontendExport frontend, ImmutableArray<StudioPageRegistration> pages,
        ImmutableArray<StudioViewRegistration> views, ImmutableArray<StudioResourceRegistration> resources,
        ImmutableArray<StudioCommandRegistration> commands, ImmutableArray<StudioLinkRegistration> links,
        ImmutableArray<StudioFrameworkClientRegistration> clients, ImmutableArray<StudioGrantRequirement> grants,
        StudioModuleLimits limits)
    { Identity = identity; OwningApplicationId = application; OwningModuleId = owner; DisplayNameMessageId = display; ModuleClass = moduleClass; Asset = asset; Frontend = frontend;
      Pages = pages; Views = views; Resources = resources; Commands = commands; Links = links; Clients = clients; Grants = grants; Limits = limits; }
    /// <summary>Gets the module identity and checksum.</summary>
    public StudioModuleIdentity Identity { get; }
    /// <summary>Gets the owning application identity.</summary>
    public string OwningApplicationId { get; }
    /// <summary>Gets the owning framework module identity.</summary>
    public string OwningModuleId { get; }
    /// <summary>Gets the localized display-name identity.</summary>
    public string DisplayNameMessageId { get; }
    /// <summary>Gets navigation ownership classification.</summary>
    public StudioModuleClass ModuleClass { get; }
    /// <summary>Gets the static executable asset manifest.</summary>
    public StudioAssetManifest Asset { get; }
    /// <summary>Gets the authorization-neutral static frontend ABI.</summary>
    public StudioFrontendExport Frontend { get; }
    /// <summary>Gets pages in ordinal identity/version order.</summary>
    public ImmutableArray<StudioPageRegistration> Pages { get; }
    /// <summary>Gets generated views in ordinal identity/version order.</summary>
    public ImmutableArray<StudioViewRegistration> Views { get; }
    /// <summary>Gets resource resolvers in discriminator order.</summary>
    public ImmutableArray<StudioResourceRegistration> Resources { get; }
    /// <summary>Gets commands in ordinal identity/version order.</summary>
    public ImmutableArray<StudioCommandRegistration> Commands { get; }
    /// <summary>Gets typed link registrations.</summary>
    public ImmutableArray<StudioLinkRegistration> Links { get; }
    /// <summary>Gets framework-client slots in ordinal identity/version order.</summary>
    public ImmutableArray<StudioFrameworkClientRegistration> Clients { get; }
    /// <summary>Gets module discovery grants.</summary>
    public ImmutableArray<StudioGrantRequirement> Grants { get; }
    /// <summary>Gets frozen module capacities.</summary>
    public StudioModuleLimits Limits { get; }

    /// <summary>Creates, cross-validates, and checksums one framework-owned module graph.</summary>
    public static StudioModuleRegistration CreateFramework(string moduleId, int version, string applicationId,
        string owningModuleId, string displayNameMessageId, StudioAssetManifest asset, StudioFrontendExport frontend,
        IEnumerable<StudioPageRegistration> pages, IEnumerable<StudioViewRegistration> views,
        IEnumerable<StudioResourceRegistration> resources, IEnumerable<StudioCommandRegistration> commands,
        IEnumerable<StudioLinkRegistration> links, IEnumerable<StudioFrameworkClientRegistration> clients,
        IEnumerable<StudioGrantRequirement> grants, StudioModuleLimits limits)
    {
        if (StringComparer.Ordinal.Equals(moduleId, "base"))
            throw new ArgumentException("The reserved BASE module cannot be authored through the framework factory.", nameof(moduleId));
        return CreateCore(moduleId, version, applicationId, owningModuleId, displayNameMessageId,
            StudioModuleClass.Framework, asset, frontend, pages, views, resources, commands, links, clients, grants, limits);
    }

    internal static StudioModuleRegistration CreateBase(string applicationId, StudioAssetManifest asset, StudioFrontendExport frontend,
        IEnumerable<StudioPageRegistration> pages, IEnumerable<StudioViewRegistration> views,
        IEnumerable<StudioResourceRegistration> resources, IEnumerable<StudioCommandRegistration> commands,
        IEnumerable<StudioLinkRegistration> links, IEnumerable<StudioFrameworkClientRegistration> clients,
        IEnumerable<StudioGrantRequirement> grants, StudioModuleLimits limits)
        => CreateCore("base", 1, applicationId, "base", "studio.module.base", StudioModuleClass.Base,
            asset, frontend, pages, views, resources, commands, links, clients, grants, limits);

    private static StudioModuleRegistration CreateCore(string moduleId, int version, string applicationId,
        string owningModuleId, string displayNameMessageId, StudioModuleClass moduleClass, StudioAssetManifest asset, StudioFrontendExport frontend,
        IEnumerable<StudioPageRegistration> pages, IEnumerable<StudioViewRegistration> views,
        IEnumerable<StudioResourceRegistration> resources, IEnumerable<StudioCommandRegistration> commands,
        IEnumerable<StudioLinkRegistration> links, IEnumerable<StudioFrameworkClientRegistration> clients,
        IEnumerable<StudioGrantRequirement> grants, StudioModuleLimits limits)
    {
        StudioContractValidation.Id(moduleId); StudioContractValidation.Id(applicationId); StudioContractValidation.Id(owningModuleId);
        StudioContractValidation.Id(displayNameMessageId);
        if (version < 1) throw new ArgumentOutOfRangeException(nameof(version));
        ArgumentNullException.ThrowIfNull(asset); ArgumentNullException.ThrowIfNull(frontend); ArgumentNullException.ThrowIfNull(limits);
        bool semanticClientOnly = moduleClass == StudioModuleClass.Framework;
        ImmutableArray<StudioPageRegistration> ownedPages = StudioGraphValidation.Ordered(pages, limits.MaximumPages,
            static value => (value.PageId, value.Version), nameof(pages), semanticClientOnly);
        ImmutableArray<StudioViewRegistration> ownedViews = StudioGraphValidation.Ordered(views, limits.MaximumViews,
            static value => (value.ViewId, value.Version), nameof(views), semanticClientOnly);
        ImmutableArray<StudioResourceRegistration> ownedResources = StudioContractValidation.Materialize(resources, limits.MaximumResources, semanticClientOnly, nameof(resources));
        if (!ownedResources.Select(static value => (byte)value.Kind).SequenceEqual(ownedResources.Select(static value => (byte)value.Kind).Order()) ||
            ownedResources.Select(static value => value.Kind).Distinct().Count() != ownedResources.Length)
            throw new ArgumentException("Studio resources are not canonical.", nameof(resources));
        ImmutableArray<StudioCommandRegistration> ownedCommands = StudioGraphValidation.Ordered(commands, limits.MaximumCommands,
            static value => (value.CommandId, value.Version), nameof(commands), true);
        ImmutableArray<StudioLinkRegistration> ownedLinks = StudioContractValidation.Materialize(links, limits.MaximumLinks, true, nameof(links));
        if (!ownedLinks.Select(static value => ((byte)value.SourceKind, (byte)value.Relation, (byte)value.TargetKind, value.ResolverId)).SequenceEqual(
                ownedLinks.Select(static value => ((byte)value.SourceKind, (byte)value.Relation, (byte)value.TargetKind, value.ResolverId))
                    .OrderBy(static value => value.Item1).ThenBy(static value => value.Item2).ThenBy(static value => value.Item3).ThenBy(static value => value.ResolverId, StringComparer.Ordinal)) ||
            ownedLinks.Select(static value => (value.SourceKind, value.Relation, value.TargetKind, value.ResolverId)).Distinct().Count() != ownedLinks.Length)
            throw new ArgumentException("Studio links are not canonical.", nameof(links));
        ImmutableArray<StudioFrameworkClientRegistration> ownedClients = StudioGraphValidation.Ordered(clients, limits.MaximumClients,
            static value => (value.ClientId, value.Version), nameof(clients));
        HashSet<string> pageIds = ownedPages.Select(static value => value.PageId).ToHashSet(StringComparer.Ordinal);
        if (ownedClients.Any(client => client.OwningPageIds.Any(page => !pageIds.Contains(page))))
            throw new ArgumentException("Studio framework-client ownership contains an unregistered page.", nameof(clients));
        ImmutableArray<StudioGrantRequirement> ownedGrants = StudioGraphValidation.Grants(grants, nameof(grants));
        StudioGraphValidation.CrossValidate(ownedPages, ownedViews, ownedResources, ownedCommands, moduleClass);
        frontend.RequireCorrespondence(new StudioModuleRegistration(
            new StudioModuleIdentity(moduleId, version, frontend.FrontendAbiChecksum), applicationId, owningModuleId,
            displayNameMessageId, moduleClass, asset, frontend, ownedPages, ownedViews, ownedResources, ownedCommands,
            ownedLinks, ownedClients, ownedGrants, limits));
        StudioSha256 checksum = StudioCanonicalEncoding.Hash("base.studio.module.v1", writer =>
        {
            writer.String(moduleId); writer.Int32(version); writer.String(applicationId); writer.String(owningModuleId); writer.String(displayNameMessageId); writer.Enum(moduleClass);
            writer.Checksum(asset.AssetGraphChecksum); writer.Checksum(frontend.FrontendAbiChecksum);
            StudioGraphValidation.Encode(writer, ownedPages, static value => value.Checksum);
            StudioGraphValidation.Encode(writer, ownedViews, static value => value.Checksum);
            StudioGraphValidation.Encode(writer, ownedResources, static value => value.Checksum);
            StudioGraphValidation.Encode(writer, ownedCommands, static value => value.Checksum);
            StudioGraphValidation.Encode(writer, ownedLinks, static value => value.Checksum);
            StudioGraphValidation.Encode(writer, ownedClients, static value => value.Checksum);
            StudioGraphValidation.Encode(writer, ownedGrants, static value => value.Checksum);
            writer.Checksum(limits.Checksum);
        });
        return new(new StudioModuleIdentity(moduleId, version, checksum), applicationId, owningModuleId,
            displayNameMessageId, moduleClass, asset, frontend, ownedPages, ownedViews, ownedResources, ownedCommands, ownedLinks, ownedClients, ownedGrants, limits);
    }
}

internal static class StudioGraphValidation
{
    internal static ImmutableArray<StudioGrantRequirement> Grants(IEnumerable<StudioGrantRequirement> grants, string parameter)
    {
        ImmutableArray<StudioGrantRequirement> values = StudioContractValidation.Materialize(grants, 128, true, parameter);
        if (!values.Select(static value => (value.GrantId, value.Version)).SequenceEqual(
                values.Select(static value => (value.GrantId, value.Version)).OrderBy(static value => value.GrantId, StringComparer.Ordinal).ThenBy(static value => value.Version)) ||
            values.Select(static value => (value.GrantId, value.Version)).Distinct().Count() != values.Length)
            throw new ArgumentException("Studio grants are not canonical.", parameter);
        return values;
    }

    internal static ImmutableArray<T> Ordered<T>(IEnumerable<T> source, int maximum,
        Func<T, (string Id, int Version)> key, string parameter, bool allowEmpty = false)
    {
        ImmutableArray<T> values = StudioContractValidation.Materialize(source, maximum, allowEmpty, parameter);
        var keys = values.Select(key).ToArray();
        if (!keys.SequenceEqual(keys.OrderBy(static value => value.Id, StringComparer.Ordinal).ThenBy(static value => value.Version)) || keys.Distinct().Count() != keys.Length)
            throw new ArgumentException("Studio registrations are not in canonical identity/version order.", parameter);
        return values;
    }

    internal static ImmutableArray<T> OrderedIdentity<T>(IEnumerable<T> source, int maximum,
        Func<T, string> key, string parameter)
    {
        ImmutableArray<T> values = StudioContractValidation.Materialize(source, maximum, true, parameter);
        string[] keys = values.Select(key).ToArray();
        if (!keys.SequenceEqual(keys.Order(StringComparer.Ordinal)) || keys.Distinct(StringComparer.Ordinal).Count() != keys.Length)
            throw new ArgumentException("Studio registrations are not in canonical identity order.", parameter);
        return values;
    }

    internal static void Encode<T>(StudioCanonicalEncoding.Writer writer, ImmutableArray<T> values, Func<T, StudioSha256> checksum)
    {
        writer.Count(values.Length); foreach (T value in values) writer.Checksum(checksum(value));
    }

    internal static void CrossValidate(ImmutableArray<StudioPageRegistration> pages, ImmutableArray<StudioViewRegistration> views,
        ImmutableArray<StudioResourceRegistration> resources, ImmutableArray<StudioCommandRegistration> commands,
        StudioModuleClass moduleClass)
    {
        var viewIds = views.Select(static value => value.ViewId).ToHashSet(StringComparer.Ordinal);
        var commandIds = commands.Select(static value => value.CommandId).ToHashSet(StringComparer.Ordinal);
        var pageIds = pages.Select(static value => value.PageId).ToHashSet(StringComparer.Ordinal);
        var resourceKinds = resources.Select(static value => value.Kind).ToHashSet();
        var referencedViews = pages.SelectMany(static page => page.Presentation.Sections)
            .SelectMany(static section => section.ViewIds).ToArray();
        var referencedCommands = pages.SelectMany(static page => page.Presentation.Sections)
            .SelectMany(static section => section.CommandIds).ToArray();
        if (referencedViews.Length != views.Length || referencedViews.GroupBy(static value => value, StringComparer.Ordinal).Any(static group => group.Count() != 1))
            throw new ArgumentException("A Studio view must have exactly one page owner.", nameof(pages));
        if (referencedCommands.Length != commands.Length || referencedCommands.GroupBy(static value => value, StringComparer.Ordinal).Any(static group => group.Count() != 1))
            throw new ArgumentException("A Studio command must have exactly one page owner.", nameof(pages));
        if (pages.Select(static value => value.Route.TemplateId).Distinct(StringComparer.Ordinal).Count() != pages.Length ||
            HasRouteOverlap(pages))
            throw new ArgumentException("Studio page routes are not unique.", nameof(pages));
        if (pages.SelectMany(static page => page.Presentation.Sections).SelectMany(static section => section.ViewIds).Any(id => !viewIds.Contains(id)) ||
            pages.SelectMany(static page => page.Presentation.Sections).SelectMany(static section => section.CommandIds).Any(id => !commandIds.Contains(id)) ||
            pages.Any(page => page.Presentation.ResourceRail is not null && !viewIds.Contains(page.Presentation.ResourceRail.ViewId)) ||
            pages.SelectMany(static page => page.Presentation.ContextualDetail?.DetailPageIds ?? []).Any(id => !pageIds.Contains(id)) ||
            pages.SelectMany(static page => page.AcceptedResources).Any(kind => !resourceKinds.Contains(kind)) ||
            views.Any(view => view.Presentation.Grid?.RowCommandIds.Any(id => !commandIds.Contains(id)) == true))
            throw new ArgumentException("Studio module graph contains a dangling registration.");
        foreach (StudioViewRegistration view in views)
        {
            if (view.Presentation.Grid is { } grid &&
                (grid.RowKind != view.ItemKind || !StringComparer.Ordinal.Equals(grid.RowNodeId, view.ItemNodeId) ||
                 !StudioSha256.FixedTimeEquals(grid.RowNodeChecksum, view.ItemNodeChecksum)))
                throw new ArgumentException("A Studio grid differs from its owning view item contract.", nameof(views));
            if (view.Presentation.Chart is { } chart)
            {
                StudioViewRegistration? bucket = views.SingleOrDefault(value => StringComparer.Ordinal.Equals(value.ViewId, chart.BucketViewId));
                StudioViewRegistration? table = views.SingleOrDefault(value => StringComparer.Ordinal.Equals(value.ViewId, chart.EquivalentTableViewId));
                if (bucket is null || table is null ||
                    !StudioSha256.FixedTimeEquals(bucket.DisclosureChannelChecksum, chart.DisclosureChannelChecksum) ||
                    !StudioSha256.FixedTimeEquals(table.DisclosureChannelChecksum, chart.DisclosureChannelChecksum) ||
                    !EquivalentFilters(bucket.Filters, table.Filters))
                    throw new ArgumentException("A Studio chart is not bound to equivalent bucket and table views.", nameof(views));
            }
        }
        foreach (StudioPageRegistration page in pages)
        {
            var ownedViewIds = page.Presentation.Sections.SelectMany(static section => section.ViewIds).ToHashSet(StringComparer.Ordinal);
            var ownedCommandIds = page.Presentation.Sections.SelectMany(static section => section.CommandIds).ToHashSet(StringComparer.Ordinal);
            if (page.Presentation.ResourceRail is { } rail &&
                (!ownedViewIds.Contains(rail.ViewId) || views.Single(value => StringComparer.Ordinal.Equals(value.ViewId, rail.ViewId)).Presentation.Grid?.RowKind != rail.ItemKind))
                throw new ArgumentException("A Studio rail differs from its page-owned view.", nameof(pages));
            if (ownedViewIds.Select(id => views.Single(value => StringComparer.Ordinal.Equals(value.ViewId, id)))
                .Any(view => view.Presentation.Grid?.RowCommandIds.Any(id => !ownedCommandIds.Contains(id)) == true))
                throw new ArgumentException("A Studio grid references a command not owned by its page.", nameof(pages));
            if (page.Presentation.ContextualDetail is { } detail)
            {
                foreach (StudioResourceKind kind in detail.AcceptedKinds)
                {
                    if (!page.AcceptedResources.Contains(kind) || !detail.DetailPageIds.Any(id =>
                            pages.Single(value => StringComparer.Ordinal.Equals(value.PageId, id)).AcceptedResources.Contains(kind)))
                        throw new ArgumentException("Studio contextual-detail authority differs from its page/resource graph.", nameof(pages));
                }
            }
        }
        if (moduleClass == StudioModuleClass.Framework && pages.Any(static page => page.Presentation.NavigationRole == StudioNavigationRole.AreaLanding))
            throw new ArgumentException("Framework Studio modules cannot own task-area landing pages.", nameof(pages));
        foreach (StudioArea area in moduleClass == StudioModuleClass.Base ? pages.Select(static value => value.Area).Distinct() : [])
        {
            if (pages.Count(page => page.Area == area && page.Presentation.NavigationRole == StudioNavigationRole.AreaLanding) != 1)
                throw new ArgumentException("Every disclosed Studio area requires exactly one landing page.", nameof(pages));
        }
    }

    private static bool EquivalentFilters(ImmutableArray<StudioFilterRegistration> left,
        ImmutableArray<StudioFilterRegistration> right)
        => left.Length == right.Length && left.Zip(right).All(static pair =>
            StringComparer.Ordinal.Equals(pair.First.FilterId, pair.Second.FilterId) &&
            StudioSha256.FixedTimeEquals(pair.First.Checksum, pair.Second.Checksum));

    internal static bool HasRouteOverlap(IEnumerable<StudioPageRegistration> pages)
    {
        StudioPageRegistration[] values = pages.ToArray();
        for (int left = 0; left < values.Length; left++)
            for (int right = left + 1; right < values.Length; right++)
                if (values[left].Route.Overlaps(values[right].Route)) return true;
        return false;
    }
}
