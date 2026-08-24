using System.Collections.Immutable;

namespace HPD.AI.Platform.Studio;

/// <summary>Identifies a Studio page's semantic rendering class.</summary>
public enum BaseStudioPageKind : byte { Overview = 1, Collection, ResourceList, ResourceDetail, Timeline, QueryTool, Action, Diagnostics }
/// <summary>Identifies the maximum disclosure admitted by a graph registration.</summary>
public enum BaseStudioDisclosureClass : byte { PublicSafe = 1, AuthorizedMetadata, ProtectedValue, ConfidentialValue }
/// <summary>Identifies whether a framework client is mandatory for module activation.</summary>
public enum BaseStudioContractNecessity : byte { Required = 1, Optional }
/// <summary>Identifies the only client protocols the Studio shell can instantiate.</summary>
public enum BaseStudioFrameworkClientProtocol : byte { BaseL41DynamicMap = 1, FrameworkGeneratedContractV1 }
/// <summary>Identifies the transport authority admitted to a generated Studio client.</summary>
public enum BaseStudioFrameworkClientTransportClass : byte { SameOriginShellAuthenticated = 1 }
/// <summary>Identifies navigation ownership derived from the reserved module identity.</summary>
public enum BaseStudioModuleClass : byte { Base = 1, Framework }
/// <summary>Identifies a Studio command's minimum interaction ceremony.</summary>
public enum BaseStudioActionClass : byte { Routine = 1, OperationalTransition, Maintenance, Destructive, DisasterOrRecoveryDomain }
/// <summary>Identifies one typed relation between Studio resources.</summary>
public enum BaseStudioLinkRelation : byte
{
    Owns = 1, ContainedBy, Affected, ProducedBy, ReceiptFor, ScheduledBy, OccurrenceOf,
    AttemptOf, ChildOf, References, LifecycleOf, Blocks, AcknowledgedBy, IndexedBy,
    StoredBy, AuthorizedBy, Diagnoses, Remediates,
}
/// <summary>Identifies canonical Studio view ordering direction.</summary>
public enum BaseStudioOrderDirection : byte { Ascending = 1, Descending }
/// <summary>Identifies canonical placement of missing and null values.</summary>
public enum BaseStudioNullPlacement : byte { MissingThenNull = 1, NullThenMissing, ValuesThenMissingThenNull, ValuesThenNullThenMissing }

/// <summary>Defines one canonical ordering member.</summary>
public sealed class BaseStudioOrderMember
{
    private BaseStudioOrderMember(string property, BaseStudioOrderDirection direction, BaseStudioNullPlacement nulls, BaseStudioSha256 checksum)
    { StablePropertyId = property; Direction = direction; NullPlacement = nulls; Checksum = checksum; }
    /// <summary>Gets the stable property identity.</summary>
    public string StablePropertyId { get; }
    /// <summary>Gets ordering direction.</summary>
    public BaseStudioOrderDirection Direction { get; }
    /// <summary>Gets missing/null placement.</summary>
    public BaseStudioNullPlacement NullPlacement { get; }
    /// <summary>Gets the canonical checksum.</summary>
    public BaseStudioSha256 Checksum { get; }
    /// <summary>Creates one canonical ordering member.</summary>
    public static BaseStudioOrderMember Create(string property, BaseStudioOrderDirection direction, BaseStudioNullPlacement nulls)
    {
        StudioContractValidation.Id(property); StudioContractValidation.Enum(direction); StudioContractValidation.Enum(nulls);
        BaseStudioSha256 checksum = StudioCanonicalEncoding.Hash("base.studio.order-member.v1", writer =>
        { writer.String(property); writer.Enum(direction); writer.Enum(nulls); });
        return new(property, direction, nulls, checksum);
    }
}

/// <summary>Defines one closed generated filter variant.</summary>
public sealed class BaseStudioFilterRegistration
{
    private BaseStudioFilterRegistration(string id, string node, BaseStudioSha256 nodeChecksum, BaseStudioSha256 checksum)
    { FilterId = id; RequestNodeId = node; RequestNodeChecksum = nodeChecksum; Checksum = checksum; }
    /// <summary>Gets the filter identity.</summary>
    public string FilterId { get; }
    /// <summary>Gets the exact L41 request-node identity.</summary>
    public string RequestNodeId { get; }
    /// <summary>Gets the request-node checksum.</summary>
    public BaseStudioSha256 RequestNodeChecksum { get; }
    /// <summary>Gets the canonical checksum.</summary>
    public BaseStudioSha256 Checksum { get; }
    /// <summary>Creates one exact filter variant.</summary>
    public static BaseStudioFilterRegistration Create(string id, string node, BaseStudioSha256 nodeChecksum)
    {
        StudioContractValidation.Id(id); StudioContractValidation.Id(node); ArgumentNullException.ThrowIfNull(nodeChecksum);
        BaseStudioSha256 owned = BaseStudioSha256.FromBytes(nodeChecksum.ToArray());
        BaseStudioSha256 checksum = StudioCanonicalEncoding.Hash("base.studio.filter.v1", writer =>
        { writer.String(id); writer.String(node); writer.Checksum(owned); });
        return new(id, node, owned, checksum);
    }
}

/// <summary>Defines one closed generated sort variant.</summary>
public sealed class BaseStudioSortRegistration
{
    private BaseStudioSortRegistration(string id, ImmutableArray<BaseStudioOrderMember> members, BaseStudioSha256 checksum)
    { SortId = id; Members = members; Checksum = checksum; }
    /// <summary>Gets the sort identity.</summary>
    public string SortId { get; }
    /// <summary>Gets its complete canonical ordering tuple.</summary>
    public ImmutableArray<BaseStudioOrderMember> Members { get; }
    /// <summary>Gets the canonical checksum.</summary>
    public BaseStudioSha256 Checksum { get; }
    /// <summary>Creates one exact sort variant.</summary>
    public static BaseStudioSortRegistration Create(string id, IEnumerable<BaseStudioOrderMember> members)
    {
        StudioContractValidation.Id(id);
        ImmutableArray<BaseStudioOrderMember> owned = StudioContractValidation.Materialize(members, 16, false, nameof(members));
        if (owned.Select(static value => value.StablePropertyId).Distinct(StringComparer.Ordinal).Count() != owned.Length)
            throw new ArgumentException("A Studio sort repeats an ordering property.", nameof(members));
        BaseStudioSha256 checksum = StudioCanonicalEncoding.Hash("base.studio.sort.v1", writer =>
        { writer.String(id); writer.Count(owned.Length); foreach (BaseStudioOrderMember value in owned) writer.Checksum(value.Checksum); });
        return new(id, owned, checksum);
    }
}

/// <summary>Defines the protected-scope rule carried by an installed Studio grant requirement.</summary>
public enum BaseStudioProtectedScopeRule : byte
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
public sealed class BaseStudioGrantRequirement
{
    private BaseStudioGrantRequirement(string id, int version, BaseStudioSha256 registration, string operation,
        string audience, string subject, string application, string module, BaseStudioResourceKind? resource,
        BaseStudioProtectedScopeRule scope, bool underlying, BaseStudioSha256 checksum)
    { GrantId = id; Version = version; RegistrationChecksum = registration; OperationId = operation; Audience = audience;
      SubjectKind = subject; ApplicationId = application; OwningModuleId = module; ResourceKind = resource;
      ScopeRule = scope; RequiresUnderlyingOperationGrant = underlying; Checksum = checksum; }
    /// <summary>Gets the grant identity.</summary>
    public string GrantId { get; }
    /// <summary>Gets the grant version.</summary>
    public int Version { get; }
    /// <summary>Gets the exact installed L38 grant-registration checksum.</summary>
    public BaseStudioSha256 RegistrationChecksum { get; }
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
    public BaseStudioResourceKind? ResourceKind { get; }
    /// <summary>Gets the exact protected-scope composition rule.</summary>
    public BaseStudioProtectedScopeRule ScopeRule { get; }
    /// <summary>Gets whether the underlying subsystem operation grant must also be admitted.</summary>
    public bool RequiresUnderlyingOperationGrant { get; }
    /// <summary>Gets the canonical checksum.</summary>
    public BaseStudioSha256 Checksum { get; }
    /// <summary>Creates one exact grant requirement.</summary>
    public static BaseStudioGrantRequirement Create(string id, int version, BaseStudioSha256 registrationChecksum,
        string operationId, string audience, string subjectKind, string applicationId, string owningModuleId,
        BaseStudioResourceKind? resourceKind, BaseStudioProtectedScopeRule scopeRule, bool requiresUnderlyingOperationGrant)
    {
        StudioContractValidation.Id(id); StudioContractValidation.Id(operationId); StudioContractValidation.Id(audience);
        StudioContractValidation.Id(subjectKind); StudioContractValidation.Id(applicationId); StudioContractValidation.Id(owningModuleId);
        ArgumentNullException.ThrowIfNull(registrationChecksum); StudioContractValidation.Enum(scopeRule);
        if (version < 1 || resourceKind is { } kind && !Enum.IsDefined(kind)) throw new ArgumentOutOfRangeException(nameof(version));
        BaseStudioSha256 registration = BaseStudioSha256.FromBytes(registrationChecksum.ToArray());
        BaseStudioSha256 checksum = StudioCanonicalEncoding.Hash("base.studio.grant.v1", writer =>
        { writer.String(id); writer.Int32(version); writer.Checksum(registration); writer.String(operationId); writer.String(audience);
          writer.String(subjectKind); writer.String(applicationId); writer.String(owningModuleId); writer.Boolean(resourceKind.HasValue);
          if (resourceKind.HasValue) writer.Enum(resourceKind.Value); writer.Enum(scopeRule); writer.Boolean(requiresUnderlyingOperationGrant); });
        return new(id, version, registration, operationId, audience, subjectKind, applicationId, owningModuleId,
            resourceKind, scopeRule, requiresUnderlyingOperationGrant, checksum);
    }
}

/// <summary>Defines one generated framework-client slot.</summary>
public sealed class BaseStudioFrameworkClientRegistration
{
    private BaseStudioFrameworkClientRegistration(string id, int version, BaseStudioContractNecessity necessity,
        BaseStudioFrameworkClientProtocol protocol, BaseStudioSha256 runtimeAbi, BaseStudioSha256 contract,
        BaseStudioSha256 operations, string endpointSurface, BaseStudioFrameworkClientTransportClass transport,
        ImmutableArray<string> pages, BaseStudioFrameworkClientLimits limits, BaseStudioSha256 checksum)
    { ClientId = id; Version = version; Necessity = necessity; Protocol = protocol; StaticRuntimeAbiChecksum = runtimeAbi;
      GeneratedContractChecksum = contract; OperationInventoryChecksum = operations; EndpointSurfaceId = endpointSurface;
      TransportClass = transport; OwningPageIds = pages; Limits = limits; Checksum = checksum; }
    /// <summary>Gets the client identity.</summary>
    public string ClientId { get; }
    /// <summary>Gets the client version.</summary>
    public int Version { get; }
    /// <summary>Gets whether the client is required.</summary>
    public BaseStudioContractNecessity Necessity { get; }
    /// <summary>Gets the closed generated-client protocol.</summary>
    public BaseStudioFrameworkClientProtocol Protocol { get; }
    /// <summary>Gets the static runtime ABI checksum.</summary>
    public BaseStudioSha256 StaticRuntimeAbiChecksum { get; }
    /// <summary>Gets the installed graph contract checksum.</summary>
    public BaseStudioSha256 GeneratedContractChecksum { get; }
    /// <summary>Gets the exact generated operation-inventory checksum.</summary>
    public BaseStudioSha256 OperationInventoryChecksum { get; }
    /// <summary>Gets the server-installed endpoint surface identity.</summary>
    public string EndpointSurfaceId { get; }
    /// <summary>Gets the only admitted browser transport class.</summary>
    public BaseStudioFrameworkClientTransportClass TransportClass { get; }
    /// <summary>Gets the pages that are permitted to consume this client.</summary>
    public ImmutableArray<string> OwningPageIds { get; }
    /// <summary>Gets the exact bounded client authority.</summary>
    public BaseStudioFrameworkClientLimits Limits { get; }
    /// <summary>Gets the registration checksum.</summary>
    public BaseStudioSha256 Checksum { get; }
    /// <summary>Creates one generated framework-client registration.</summary>
    public static BaseStudioFrameworkClientRegistration Create(string id, int version, BaseStudioContractNecessity necessity,
        BaseStudioFrameworkClientProtocol protocol, BaseStudioSha256 runtimeAbi, BaseStudioSha256 contract,
        BaseStudioSha256 operations, string endpointSurface, BaseStudioFrameworkClientTransportClass transport,
        IEnumerable<string> owningPages, BaseStudioFrameworkClientLimits limits)
    {
        StudioContractValidation.Id(id); StudioContractValidation.Enum(necessity); StudioContractValidation.Enum(protocol);
        StudioContractValidation.Id(endpointSurface); StudioContractValidation.Enum(transport);
        if (version < 1) throw new ArgumentOutOfRangeException(nameof(version));
        ArgumentNullException.ThrowIfNull(runtimeAbi); ArgumentNullException.ThrowIfNull(contract);
        ArgumentNullException.ThrowIfNull(operations); ArgumentNullException.ThrowIfNull(limits);
        BaseStudioSha256 ownedAbi = BaseStudioSha256.FromBytes(runtimeAbi.ToArray());
        BaseStudioSha256 ownedContract = BaseStudioSha256.FromBytes(contract.ToArray());
        BaseStudioSha256 ownedOperations = BaseStudioSha256.FromBytes(operations.ToArray());
        ImmutableArray<string> pages = StudioContractValidation.Ids(owningPages, 64, false, nameof(owningPages));
        BaseStudioSha256 checksum = StudioCanonicalEncoding.Hash("base.studio.frameworkClient.v1", writer =>
        { writer.String(id); writer.Int32(version); writer.Enum(necessity); writer.Enum(protocol); writer.Checksum(ownedAbi);
          writer.Checksum(ownedContract); writer.Checksum(ownedOperations); writer.String(endpointSurface); writer.Enum(transport);
          writer.Count(pages.Length); foreach (string page in pages) writer.String(page); writer.Checksum(limits.Checksum); });
        return new(id, version, necessity, protocol, ownedAbi, ownedContract, ownedOperations, endpointSurface,
            transport, pages, limits, checksum);
    }
}

/// <summary>Defines the bounded authority supplied to one shell-owned framework client.</summary>
public sealed class BaseStudioFrameworkClientLimits
{
    private BaseStudioFrameworkClientLimits(int operations, long requestBytes, long responseBytes, int concurrent,
        TimeSpan acquisition, TimeSpan operation, TimeSpan disposal, BaseStudioSha256 checksum)
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
    public BaseStudioSha256 Checksum { get; }
    /// <summary>Creates one independently bounded framework-client limit envelope.</summary>
    public static BaseStudioFrameworkClientLimits Create(int operations, long requestBytes, long responseBytes,
        int concurrent, TimeSpan acquisition, TimeSpan operation, TimeSpan disposal)
    {
        if (operations is < 1 or > 4096 || requestBytes is < 1 or > 67_108_864 || responseBytes is < 1 or > 67_108_864 ||
            concurrent is < 1 or > 256 || acquisition <= TimeSpan.Zero || acquisition > TimeSpan.FromSeconds(30) ||
            operation <= TimeSpan.Zero || operation > TimeSpan.FromMinutes(5) || disposal <= TimeSpan.Zero || disposal > TimeSpan.FromSeconds(30))
            throw new ArgumentOutOfRangeException(nameof(operations));
        BaseStudioSha256 checksum = StudioCanonicalEncoding.Hash("base.studio.frameworkClient.limits.v1", writer =>
        { writer.Int32(operations); writer.Int64(requestBytes); writer.Int64(responseBytes); writer.Int32(concurrent);
          writer.Int64(checked((long)acquisition.TotalMilliseconds)); writer.Int64(checked((long)operation.TotalMilliseconds));
          writer.Int64(checked((long)disposal.TotalMilliseconds)); });
        return new(operations, requestBytes, responseBytes, concurrent, acquisition, operation, disposal, checksum);
    }
}

/// <summary>Defines one exact generated view contract.</summary>
public sealed class BaseStudioViewRegistration
{
    private BaseStudioViewRegistration(string id, int version, string producer, string requestNode, BaseStudioSha256 requestChecksum,
        BaseStudioResourceKind itemKind, string itemNode, BaseStudioSha256 itemChecksum, string cursorPurpose, ImmutableArray<BaseStudioOrderMember> canonicalOrder,
        ImmutableArray<BaseStudioFilterRegistration> filters, ImmutableArray<BaseStudioSortRegistration> sorts,
        BaseStudioSha256 disclosureChecksum, long maximumBytes, int maximumItems,
        BaseStudioViewPresentationRegistration presentation, BaseStudioSha256 checksum)
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
    public BaseStudioSha256 RequestNodeChecksum { get; }
    /// <summary>Gets the returned resource kind.</summary>
    public BaseStudioResourceKind ItemKind { get; }
    /// <summary>Gets the L41 item-node identity.</summary>
    public string ItemNodeId { get; }
    /// <summary>Gets the item-node checksum.</summary>
    public BaseStudioSha256 ItemNodeChecksum { get; }
    /// <summary>Gets the protected cursor purpose.</summary>
    public string CursorPurpose { get; }
    /// <summary>Gets the mandatory canonical final ordering tuple.</summary>
    public ImmutableArray<BaseStudioOrderMember> CanonicalOrder { get; }
    /// <summary>Gets closed generated filter variants.</summary>
    public ImmutableArray<BaseStudioFilterRegistration> Filters { get; }
    /// <summary>Gets closed generated sort variants.</summary>
    public ImmutableArray<BaseStudioSortRegistration> Sorts { get; }
    /// <summary>Gets the disclosure-channel checksum.</summary>
    public BaseStudioSha256 DisclosureChannelChecksum { get; }
    /// <summary>Gets the maximum result bytes.</summary>
    public long MaximumBytes { get; }
    /// <summary>Gets the maximum result items.</summary>
    public int MaximumItems { get; }
    /// <summary>Gets immutable presentation mechanics.</summary>
    public BaseStudioViewPresentationRegistration Presentation { get; }
    /// <summary>Gets the canonical checksum.</summary>
    public BaseStudioSha256 Checksum { get; }

    /// <summary>Creates and checksums one generated view contract.</summary>
    public static BaseStudioViewRegistration Create(string id, int version, string producer, string requestNode,
        BaseStudioSha256 requestChecksum, BaseStudioResourceKind itemKind, string itemNode, BaseStudioSha256 itemChecksum, string cursorPurpose,
        IEnumerable<BaseStudioOrderMember> canonicalOrder, IEnumerable<BaseStudioFilterRegistration>? filters,
        IEnumerable<BaseStudioSortRegistration>? sorts, BaseStudioSha256 disclosureChecksum,
        long maximumBytes, int maximumItems,
        BaseStudioViewPresentationRegistration presentation)
    {
        StudioContractValidation.Id(id); StudioContractValidation.Id(producer); StudioContractValidation.Id(requestNode);
        StudioContractValidation.Enum(itemKind); StudioContractValidation.Id(itemNode); StudioContractValidation.Id(cursorPurpose);
        if (version < 1 || maximumBytes < 1 || maximumItems < 1) throw new ArgumentOutOfRangeException(nameof(version));
        ArgumentNullException.ThrowIfNull(requestChecksum); ArgumentNullException.ThrowIfNull(itemChecksum);
        ArgumentNullException.ThrowIfNull(disclosureChecksum); ArgumentNullException.ThrowIfNull(presentation);
        if (!StringComparer.Ordinal.Equals(id, presentation.ViewId)) throw new ArgumentException("Studio view presentation identity differs from its view.", nameof(presentation));
        ImmutableArray<BaseStudioOrderMember> ownedOrder = StudioContractValidation.Materialize(canonicalOrder, 16, false, nameof(canonicalOrder));
        if (ownedOrder.Select(static value => value.StablePropertyId).Distinct(StringComparer.Ordinal).Count() != ownedOrder.Length)
            throw new ArgumentException("Studio canonical order repeats a property.", nameof(canonicalOrder));
        ImmutableArray<BaseStudioFilterRegistration> ownedFilters = StudioGraphValidation.OrderedIdentity(filters ?? [], 64, static value => value.FilterId, nameof(filters));
        ImmutableArray<BaseStudioSortRegistration> ownedSorts = StudioGraphValidation.OrderedIdentity(sorts ?? [], 64, static value => value.SortId, nameof(sorts));
        if (presentation.Grid is not null && presentation.Grid.Columns.Any(column =>
                column.FilterId is not null && !ownedFilters.Any(value => StringComparer.Ordinal.Equals(value.FilterId, column.FilterId)) ||
                column.SortId is not null && !ownedSorts.Any(value => StringComparer.Ordinal.Equals(value.SortId, column.SortId))))
            throw new ArgumentException("Studio grid references an unregistered filter or sort.", nameof(presentation));
        BaseStudioSha256 request = BaseStudioSha256.FromBytes(requestChecksum.ToArray());
        BaseStudioSha256 item = BaseStudioSha256.FromBytes(itemChecksum.ToArray());
        BaseStudioSha256 disclosure = BaseStudioSha256.FromBytes(disclosureChecksum.ToArray());
        BaseStudioSha256 checksum = StudioCanonicalEncoding.Hash("base.studio.view.v1", writer =>
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
public sealed class BaseStudioPageRegistration
{
    private BaseStudioPageRegistration(string id, int version, BaseStudioArea area, string label,
        BaseStudioRouteTemplate route, BaseStudioPageKind kind, BaseStudioPagePresentationRegistration presentation,
        ImmutableArray<BaseStudioResourceKind> resources, ImmutableArray<string> endpoints,
        ImmutableArray<BaseStudioGrantRequirement> grants, BaseStudioDisclosureClass disclosure, BaseStudioSha256 checksum)
    { PageId = id; Version = version; Area = area; LabelMessageId = label; Route = route; Kind = kind; Presentation = presentation;
      AcceptedResources = resources; RequiredEndpointIds = endpoints; Grants = grants; Disclosure = disclosure; Checksum = checksum; }
    /// <summary>Gets the page identity.</summary>
    public string PageId { get; }
    /// <summary>Gets the page version.</summary>
    public int Version { get; }
    /// <summary>Gets the task area.</summary>
    public BaseStudioArea Area { get; }
    /// <summary>Gets the localized page-label identity.</summary>
    public string LabelMessageId { get; }
    /// <summary>Gets the route.</summary>
    public BaseStudioRouteTemplate Route { get; }
    /// <summary>Gets the semantic page kind.</summary>
    public BaseStudioPageKind Kind { get; }
    /// <summary>Gets presentation mechanics.</summary>
    public BaseStudioPagePresentationRegistration Presentation { get; }
    /// <summary>Gets accepted resource kinds.</summary>
    public ImmutableArray<BaseStudioResourceKind> AcceptedResources { get; }
    /// <summary>Gets exact required endpoint identities.</summary>
    public ImmutableArray<string> RequiredEndpointIds { get; }
    /// <summary>Gets discovery grants.</summary>
    public ImmutableArray<BaseStudioGrantRequirement> Grants { get; }
    /// <summary>Gets maximum disclosure.</summary>
    public BaseStudioDisclosureClass Disclosure { get; }
    /// <summary>Gets the canonical checksum.</summary>
    public BaseStudioSha256 Checksum { get; }

    /// <summary>Creates and checksums one exact page registration.</summary>
    public static BaseStudioPageRegistration Create(string id, int version, BaseStudioArea area, string label,
        BaseStudioRouteTemplate route, BaseStudioPageKind kind, BaseStudioPagePresentationRegistration presentation,
        IEnumerable<BaseStudioResourceKind> acceptedResources, IEnumerable<string> endpointIds,
        IEnumerable<BaseStudioGrantRequirement> grants, BaseStudioDisclosureClass disclosure)
    {
        StudioContractValidation.Id(id); StudioContractValidation.Id(label); StudioContractValidation.Enum(area);
        StudioContractValidation.Enum(kind); StudioContractValidation.Enum(disclosure);
        ArgumentNullException.ThrowIfNull(route); ArgumentNullException.ThrowIfNull(presentation);
        if (version < 1) throw new ArgumentOutOfRangeException(nameof(version));
        if (!StringComparer.Ordinal.Equals(id, presentation.PageId) || version != presentation.PageVersion)
            throw new ArgumentException("Studio page presentation owner differs from its page.", nameof(presentation));
        ImmutableArray<BaseStudioResourceKind> resources = StudioContractValidation.Materialize(acceptedResources, 64, true, nameof(acceptedResources));
        if (resources.Any(static value => !Enum.IsDefined(value)) || resources.Distinct().Count() != resources.Length ||
            !resources.SequenceEqual(resources.OrderBy(static value => (byte)value))) throw new ArgumentException("Studio resource kinds are not canonical.", nameof(acceptedResources));
        ImmutableArray<string> endpoints = StudioContractValidation.Ids(endpointIds, 128, true, nameof(endpointIds));
        ImmutableArray<BaseStudioGrantRequirement> ownedGrants = StudioGraphValidation.Grants(grants, nameof(grants));
        BaseStudioSha256 checksum = StudioCanonicalEncoding.Hash("base.studio.page.v1", writer =>
        {
            writer.String(id); writer.Int32(version); writer.Enum(area); writer.String(label); writer.Checksum(route.Checksum);
            writer.Enum(kind); writer.Checksum(presentation.Checksum); writer.Count(resources.Length); foreach (BaseStudioResourceKind value in resources) writer.Enum(value);
            writer.Count(endpoints.Length); foreach (string value in endpoints) writer.String(value);
            writer.Count(ownedGrants.Length); foreach (BaseStudioGrantRequirement value in ownedGrants) writer.Checksum(value.Checksum); writer.Enum(disclosure);
        });
        return new(id, version, area, label, route, kind, presentation, resources, endpoints, ownedGrants, disclosure, checksum);
    }
}

/// <summary>Defines one graph-owned resource resolver.</summary>
public sealed class BaseStudioResourceRegistration
{
    private BaseStudioResourceRegistration(BaseStudioResourceKind kind, string resolver, ImmutableArray<string> endpoints,
        ImmutableArray<BaseStudioGrantRequirement> grants, BaseStudioDisclosureClass disclosure, BaseStudioSha256 checksum)
    { Kind = kind; ResolverId = resolver; EndpointIds = endpoints; Grants = grants; Disclosure = disclosure; Checksum = checksum; }
    /// <summary>Gets the resource kind.</summary>
    public BaseStudioResourceKind Kind { get; }
    /// <summary>Gets the Runtime resolver identity.</summary>
    public string ResolverId { get; }
    /// <summary>Gets resolver endpoint identities.</summary>
    public ImmutableArray<string> EndpointIds { get; }
    /// <summary>Gets discovery grants.</summary>
    public ImmutableArray<BaseStudioGrantRequirement> Grants { get; }
    /// <summary>Gets maximum disclosure.</summary>
    public BaseStudioDisclosureClass Disclosure { get; }
    /// <summary>Gets the canonical checksum.</summary>
    public BaseStudioSha256 Checksum { get; }
    /// <summary>Creates and checksums one resource resolver.</summary>
    public static BaseStudioResourceRegistration Create(BaseStudioResourceKind kind, string resolver,
        IEnumerable<string> endpoints, IEnumerable<BaseStudioGrantRequirement> grants, BaseStudioDisclosureClass disclosure)
    {
        StudioContractValidation.Enum(kind); StudioContractValidation.Id(resolver); StudioContractValidation.Enum(disclosure);
        ImmutableArray<string> ownedEndpoints = StudioContractValidation.Ids(endpoints, 128, false, nameof(endpoints));
        ImmutableArray<BaseStudioGrantRequirement> ownedGrants = StudioGraphValidation.Grants(grants, nameof(grants));
        BaseStudioSha256 checksum = StudioCanonicalEncoding.Hash("base.studio.resource.v1", writer =>
        { writer.Enum(kind); writer.String(resolver); writer.Count(ownedEndpoints.Length); foreach (string value in ownedEndpoints) writer.String(value);
          writer.Count(ownedGrants.Length); foreach (BaseStudioGrantRequirement value in ownedGrants) writer.Checksum(value.Checksum); writer.Enum(disclosure); });
        return new(kind, resolver, ownedEndpoints, ownedGrants, disclosure, checksum);
    }
}

/// <summary>Defines one graph-owned, generated Studio command.</summary>
public sealed class BaseStudioCommandRegistration
{
    private BaseStudioCommandRegistration(string id, int version, string operation, string inputNode, BaseStudioSha256 inputChecksum,
        string resultNode, BaseStudioSha256 resultChecksum, BaseStudioActionClass action, ImmutableArray<BaseStudioGrantRequirement> grants,
        long maximumRequestBytes, long maximumResultBytes, BaseStudioFreshAuthenticationClass? freshAuthentication,
        ImmutableArray<BaseStudioCommandAcknowledgementRequirement> acknowledgements, BaseStudioSha256 checksum)
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
    public BaseStudioSha256 InputNodeChecksum { get; }
    /// <summary>Gets the L41 result-node identity.</summary>
    public string ResultNodeId { get; }
    /// <summary>Gets the result-node checksum.</summary>
    public BaseStudioSha256 ResultNodeChecksum { get; }
    /// <summary>Gets the minimum interaction ceremony.</summary>
    public BaseStudioActionClass ActionClass { get; }
    /// <summary>Gets execution grants.</summary>
    public ImmutableArray<BaseStudioGrantRequirement> Grants { get; }
    /// <summary>Gets the maximum request bytes.</summary>
    public long MaximumRequestBytes { get; }
    /// <summary>Gets the maximum result bytes.</summary>
    public long MaximumResultBytes { get; }
    /// <summary>Gets the required fresh-authentication class, or null when none is required.</summary>
    public BaseStudioFreshAuthenticationClass? FreshAuthentication { get; }
    /// <summary>Gets the exact acknowledgement evidence required by execution.</summary>
    public ImmutableArray<BaseStudioCommandAcknowledgementRequirement> Acknowledgements { get; }
    /// <summary>Gets the canonical checksum.</summary>
    public BaseStudioSha256 Checksum { get; }
    /// <summary>Creates and checksums one exact command.</summary>
    public static BaseStudioCommandRegistration Create(string id, int version, string operation, string inputNode,
        BaseStudioSha256 inputChecksum, string resultNode, BaseStudioSha256 resultChecksum, BaseStudioActionClass action,
        IEnumerable<BaseStudioGrantRequirement> grants, long maximumRequestBytes, long maximumResultBytes,
        BaseStudioFreshAuthenticationClass? freshAuthentication, IEnumerable<BaseStudioCommandAcknowledgementRequirement> acknowledgements)
    {
        StudioContractValidation.Id(id); StudioContractValidation.Id(operation); StudioContractValidation.Id(inputNode); StudioContractValidation.Id(resultNode);
        StudioContractValidation.Enum(action); ArgumentNullException.ThrowIfNull(inputChecksum); ArgumentNullException.ThrowIfNull(resultChecksum);
        if (version < 1 || maximumRequestBytes < 1 || maximumResultBytes < 1) throw new ArgumentOutOfRangeException(nameof(version));
        ImmutableArray<BaseStudioGrantRequirement> ownedGrants = StudioGraphValidation.Grants(grants, nameof(grants));
        if (freshAuthentication is { } authentication) StudioContractValidation.Enum(authentication);
        ImmutableArray<BaseStudioCommandAcknowledgementRequirement> ownedAcknowledgements = StudioGraphValidation.OrderedIdentity(
            acknowledgements, 32, static value => value.PurposeId, nameof(acknowledgements));
        BaseStudioSha256 input = BaseStudioSha256.FromBytes(inputChecksum.ToArray()); BaseStudioSha256 result = BaseStudioSha256.FromBytes(resultChecksum.ToArray());
        BaseStudioSha256 checksum = StudioCanonicalEncoding.Hash("base.studio.command.v1", writer =>
        { writer.String(id); writer.Int32(version); writer.String(operation); writer.String(inputNode); writer.Checksum(input);
          writer.String(resultNode); writer.Checksum(result); writer.Enum(action); writer.Count(ownedGrants.Length);
          foreach (BaseStudioGrantRequirement value in ownedGrants) writer.Checksum(value.Checksum); writer.Int64(maximumRequestBytes); writer.Int64(maximumResultBytes);
          writer.Boolean(freshAuthentication.HasValue); if (freshAuthentication.HasValue) writer.Enum(freshAuthentication.Value);
          writer.Count(ownedAcknowledgements.Length); foreach (var value in ownedAcknowledgements) writer.Checksum(value.Checksum); });
        return new(id, version, operation, inputNode, input, resultNode, result, action, ownedGrants, maximumRequestBytes, maximumResultBytes,
            freshAuthentication, ownedAcknowledgements, checksum);
    }
}

/// <summary>Defines one exact purpose-and-impact acknowledgement required by a reviewed command.</summary>
public sealed class BaseStudioCommandAcknowledgementRequirement
{
    private BaseStudioCommandAcknowledgementRequirement(string purposeId, string impactId, BaseStudioSha256 checksum)
    { PurposeId = purposeId; ImpactId = impactId; Checksum = checksum; }
    /// <summary>Gets the stable acknowledgement purpose.</summary>
    public string PurposeId { get; }
    /// <summary>Gets the stable semantic impact.</summary>
    public string ImpactId { get; }
    /// <summary>Gets the purpose-bound requirement checksum.</summary>
    public BaseStudioSha256 Checksum { get; }
    /// <summary>Creates one exact acknowledgement requirement.</summary>
    public static BaseStudioCommandAcknowledgementRequirement Create(string purposeId, string impactId)
    {
        StudioContractValidation.Id(purposeId); StudioContractValidation.Id(impactId);
        return new(new(purposeId.AsSpan()), new(impactId.AsSpan()), StudioCanonicalEncoding.Hash("base.studio.command-acknowledgement.v1", w => { w.String(purposeId); w.String(impactId); }));
    }
}

/// <summary>Defines one permitted typed relation between resource kinds.</summary>
public sealed class BaseStudioLinkRegistration
{
    private BaseStudioLinkRegistration(BaseStudioResourceKind source, BaseStudioResourceKind target,
        BaseStudioLinkRelation relation, string resolver, BaseStudioSha256 checksum)
    { SourceKind = source; TargetKind = target; Relation = relation; ResolverId = resolver; Checksum = checksum; }
    /// <summary>Gets the source kind.</summary>
    public BaseStudioResourceKind SourceKind { get; }
    /// <summary>Gets the target kind.</summary>
    public BaseStudioResourceKind TargetKind { get; }
    /// <summary>Gets the semantic relation.</summary>
    public BaseStudioLinkRelation Relation { get; }
    /// <summary>Gets the Runtime resolver identity.</summary>
    public string ResolverId { get; }
    /// <summary>Gets the canonical checksum.</summary>
    public BaseStudioSha256 Checksum { get; }
    /// <summary>Creates one typed link registration.</summary>
    public static BaseStudioLinkRegistration Create(BaseStudioResourceKind source, BaseStudioResourceKind target,
        BaseStudioLinkRelation relation, string resolver)
    {
        StudioContractValidation.Enum(source); StudioContractValidation.Enum(target); StudioContractValidation.Enum(relation); StudioContractValidation.Id(resolver);
        BaseStudioSha256 checksum = StudioCanonicalEncoding.Hash("base.studio.link.v1", writer =>
        { writer.Enum(source); writer.Enum(target); writer.Enum(relation); writer.String(resolver); });
        return new(source, target, relation, resolver, checksum);
    }
}

/// <summary>Defines bounded module-owned Studio graph capacities.</summary>
public sealed class BaseStudioModuleLimits
{
    private BaseStudioModuleLimits(int pages, int views, int resources, int commands, int links, int clients, BaseStudioSha256 checksum)
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
    public BaseStudioSha256 Checksum { get; }
    /// <summary>Creates module capacity limits.</summary>
    public static BaseStudioModuleLimits Create(int pages, int views, int resources, int commands, int links, int clients)
    {
        if (pages is < 1 or > 64 || views is < 1 or > 256 || resources is < 1 or > 128 || commands is < 0 or > 256 || links is < 0 or > 256 || clients is < 1 or > 32)
            throw new ArgumentOutOfRangeException(nameof(pages));
        BaseStudioSha256 checksum = StudioCanonicalEncoding.Hash("base.studio.module-limits.v1", writer =>
        { writer.Int32(pages); writer.Int32(views); writer.Int32(resources); writer.Int32(commands); writer.Int32(links); writer.Int32(clients); });
        return new(pages, views, resources, commands, links, clients, checksum);
    }
}

/// <summary>Identifies one frozen Studio module graph.</summary>
public sealed class BaseStudioModuleIdentity
{
    internal BaseStudioModuleIdentity(string id, int version, BaseStudioSha256 checksum)
    { ModuleId = id; Version = version; Checksum = checksum; }
    /// <summary>Gets the module identity.</summary>
    public string ModuleId { get; }
    /// <summary>Gets the module version.</summary>
    public int Version { get; }
    /// <summary>Gets the application-specific registration checksum.</summary>
    public BaseStudioSha256 Checksum { get; }
}

/// <summary>Represents one immutable, application-owned Studio module graph.</summary>
public sealed class BaseStudioModuleRegistration
{
    private BaseStudioModuleRegistration(BaseStudioModuleIdentity identity, string application, string owner, string display,
        BaseStudioModuleClass moduleClass, BaseStudioAssetManifest asset, BaseStudioFrontendExport frontend, ImmutableArray<BaseStudioPageRegistration> pages,
        ImmutableArray<BaseStudioViewRegistration> views, ImmutableArray<BaseStudioResourceRegistration> resources,
        ImmutableArray<BaseStudioCommandRegistration> commands, ImmutableArray<BaseStudioLinkRegistration> links,
        ImmutableArray<BaseStudioFrameworkClientRegistration> clients, ImmutableArray<BaseStudioGrantRequirement> grants,
        BaseStudioModuleLimits limits)
    { Identity = identity; OwningApplicationId = application; OwningModuleId = owner; DisplayNameMessageId = display; ModuleClass = moduleClass; Asset = asset; Frontend = frontend;
      Pages = pages; Views = views; Resources = resources; Commands = commands; Links = links; Clients = clients; Grants = grants; Limits = limits; }
    /// <summary>Gets the module identity and checksum.</summary>
    public BaseStudioModuleIdentity Identity { get; }
    /// <summary>Gets the owning application identity.</summary>
    public string OwningApplicationId { get; }
    /// <summary>Gets the owning framework module identity.</summary>
    public string OwningModuleId { get; }
    /// <summary>Gets the localized display-name identity.</summary>
    public string DisplayNameMessageId { get; }
    /// <summary>Gets navigation ownership classification.</summary>
    public BaseStudioModuleClass ModuleClass { get; }
    /// <summary>Gets the static executable asset manifest.</summary>
    public BaseStudioAssetManifest Asset { get; }
    /// <summary>Gets the authorization-neutral static frontend ABI.</summary>
    public BaseStudioFrontendExport Frontend { get; }
    /// <summary>Gets pages in ordinal identity/version order.</summary>
    public ImmutableArray<BaseStudioPageRegistration> Pages { get; }
    /// <summary>Gets generated views in ordinal identity/version order.</summary>
    public ImmutableArray<BaseStudioViewRegistration> Views { get; }
    /// <summary>Gets resource resolvers in discriminator order.</summary>
    public ImmutableArray<BaseStudioResourceRegistration> Resources { get; }
    /// <summary>Gets commands in ordinal identity/version order.</summary>
    public ImmutableArray<BaseStudioCommandRegistration> Commands { get; }
    /// <summary>Gets typed link registrations.</summary>
    public ImmutableArray<BaseStudioLinkRegistration> Links { get; }
    /// <summary>Gets framework-client slots in ordinal identity/version order.</summary>
    public ImmutableArray<BaseStudioFrameworkClientRegistration> Clients { get; }
    /// <summary>Gets module discovery grants.</summary>
    public ImmutableArray<BaseStudioGrantRequirement> Grants { get; }
    /// <summary>Gets frozen module capacities.</summary>
    public BaseStudioModuleLimits Limits { get; }

    /// <summary>Creates, cross-validates, and checksums one framework-owned module graph.</summary>
    public static BaseStudioModuleRegistration CreateFramework(string moduleId, int version, string applicationId,
        string owningModuleId, string displayNameMessageId, BaseStudioAssetManifest asset, BaseStudioFrontendExport frontend,
        IEnumerable<BaseStudioPageRegistration> pages, IEnumerable<BaseStudioViewRegistration> views,
        IEnumerable<BaseStudioResourceRegistration> resources, IEnumerable<BaseStudioCommandRegistration> commands,
        IEnumerable<BaseStudioLinkRegistration> links, IEnumerable<BaseStudioFrameworkClientRegistration> clients,
        IEnumerable<BaseStudioGrantRequirement> grants, BaseStudioModuleLimits limits)
    {
        if (StringComparer.Ordinal.Equals(moduleId, "base"))
            throw new ArgumentException("The reserved BASE module cannot be authored through the framework factory.", nameof(moduleId));
        return CreateCore(moduleId, version, applicationId, owningModuleId, displayNameMessageId,
            BaseStudioModuleClass.Framework, asset, frontend, pages, views, resources, commands, links, clients, grants, limits);
    }

    internal static BaseStudioModuleRegistration CreateBase(string applicationId, BaseStudioAssetManifest asset, BaseStudioFrontendExport frontend,
        IEnumerable<BaseStudioPageRegistration> pages, IEnumerable<BaseStudioViewRegistration> views,
        IEnumerable<BaseStudioResourceRegistration> resources, IEnumerable<BaseStudioCommandRegistration> commands,
        IEnumerable<BaseStudioLinkRegistration> links, IEnumerable<BaseStudioFrameworkClientRegistration> clients,
        IEnumerable<BaseStudioGrantRequirement> grants, BaseStudioModuleLimits limits)
        => CreateCore("base", 1, applicationId, "base", "studio.module.base", BaseStudioModuleClass.Base,
            asset, frontend, pages, views, resources, commands, links, clients, grants, limits);

    private static BaseStudioModuleRegistration CreateCore(string moduleId, int version, string applicationId,
        string owningModuleId, string displayNameMessageId, BaseStudioModuleClass moduleClass, BaseStudioAssetManifest asset, BaseStudioFrontendExport frontend,
        IEnumerable<BaseStudioPageRegistration> pages, IEnumerable<BaseStudioViewRegistration> views,
        IEnumerable<BaseStudioResourceRegistration> resources, IEnumerable<BaseStudioCommandRegistration> commands,
        IEnumerable<BaseStudioLinkRegistration> links, IEnumerable<BaseStudioFrameworkClientRegistration> clients,
        IEnumerable<BaseStudioGrantRequirement> grants, BaseStudioModuleLimits limits)
    {
        StudioContractValidation.Id(moduleId); StudioContractValidation.Id(applicationId); StudioContractValidation.Id(owningModuleId);
        StudioContractValidation.Id(displayNameMessageId);
        if (version < 1) throw new ArgumentOutOfRangeException(nameof(version));
        ArgumentNullException.ThrowIfNull(asset); ArgumentNullException.ThrowIfNull(frontend); ArgumentNullException.ThrowIfNull(limits);
        bool semanticClientOnly = moduleClass == BaseStudioModuleClass.Framework;
        ImmutableArray<BaseStudioPageRegistration> ownedPages = StudioGraphValidation.Ordered(pages, limits.MaximumPages,
            static value => (value.PageId, value.Version), nameof(pages), semanticClientOnly);
        ImmutableArray<BaseStudioViewRegistration> ownedViews = StudioGraphValidation.Ordered(views, limits.MaximumViews,
            static value => (value.ViewId, value.Version), nameof(views), semanticClientOnly);
        ImmutableArray<BaseStudioResourceRegistration> ownedResources = StudioContractValidation.Materialize(resources, limits.MaximumResources, semanticClientOnly, nameof(resources));
        if (!ownedResources.Select(static value => (byte)value.Kind).SequenceEqual(ownedResources.Select(static value => (byte)value.Kind).Order()) ||
            ownedResources.Select(static value => value.Kind).Distinct().Count() != ownedResources.Length)
            throw new ArgumentException("Studio resources are not canonical.", nameof(resources));
        ImmutableArray<BaseStudioCommandRegistration> ownedCommands = StudioGraphValidation.Ordered(commands, limits.MaximumCommands,
            static value => (value.CommandId, value.Version), nameof(commands), true);
        ImmutableArray<BaseStudioLinkRegistration> ownedLinks = StudioContractValidation.Materialize(links, limits.MaximumLinks, true, nameof(links));
        if (!ownedLinks.Select(static value => ((byte)value.SourceKind, (byte)value.Relation, (byte)value.TargetKind, value.ResolverId)).SequenceEqual(
                ownedLinks.Select(static value => ((byte)value.SourceKind, (byte)value.Relation, (byte)value.TargetKind, value.ResolverId))
                    .OrderBy(static value => value.Item1).ThenBy(static value => value.Item2).ThenBy(static value => value.Item3).ThenBy(static value => value.ResolverId, StringComparer.Ordinal)) ||
            ownedLinks.Select(static value => (value.SourceKind, value.Relation, value.TargetKind, value.ResolverId)).Distinct().Count() != ownedLinks.Length)
            throw new ArgumentException("Studio links are not canonical.", nameof(links));
        ImmutableArray<BaseStudioFrameworkClientRegistration> ownedClients = StudioGraphValidation.Ordered(clients, limits.MaximumClients,
            static value => (value.ClientId, value.Version), nameof(clients));
        HashSet<string> pageIds = ownedPages.Select(static value => value.PageId).ToHashSet(StringComparer.Ordinal);
        if (ownedClients.Any(client => client.OwningPageIds.Any(page => !pageIds.Contains(page))))
            throw new ArgumentException("Studio framework-client ownership contains an unregistered page.", nameof(clients));
        ImmutableArray<BaseStudioGrantRequirement> ownedGrants = StudioGraphValidation.Grants(grants, nameof(grants));
        StudioGraphValidation.CrossValidate(ownedPages, ownedViews, ownedResources, ownedCommands, moduleClass);
        frontend.RequireCorrespondence(new BaseStudioModuleRegistration(
            new BaseStudioModuleIdentity(moduleId, version, frontend.FrontendAbiChecksum), applicationId, owningModuleId,
            displayNameMessageId, moduleClass, asset, frontend, ownedPages, ownedViews, ownedResources, ownedCommands,
            ownedLinks, ownedClients, ownedGrants, limits));
        BaseStudioSha256 checksum = StudioCanonicalEncoding.Hash("base.studio.module.v1", writer =>
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
        return new(new BaseStudioModuleIdentity(moduleId, version, checksum), applicationId, owningModuleId,
            displayNameMessageId, moduleClass, asset, frontend, ownedPages, ownedViews, ownedResources, ownedCommands, ownedLinks, ownedClients, ownedGrants, limits);
    }
}

internal static class StudioGraphValidation
{
    internal static ImmutableArray<BaseStudioGrantRequirement> Grants(IEnumerable<BaseStudioGrantRequirement> grants, string parameter)
    {
        ImmutableArray<BaseStudioGrantRequirement> values = StudioContractValidation.Materialize(grants, 128, true, parameter);
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

    internal static void Encode<T>(StudioCanonicalEncoding.Writer writer, ImmutableArray<T> values, Func<T, BaseStudioSha256> checksum)
    {
        writer.Count(values.Length); foreach (T value in values) writer.Checksum(checksum(value));
    }

    internal static void CrossValidate(ImmutableArray<BaseStudioPageRegistration> pages, ImmutableArray<BaseStudioViewRegistration> views,
        ImmutableArray<BaseStudioResourceRegistration> resources, ImmutableArray<BaseStudioCommandRegistration> commands,
        BaseStudioModuleClass moduleClass)
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
        foreach (BaseStudioViewRegistration view in views)
        {
            if (view.Presentation.Grid is { } grid &&
                (grid.RowKind != view.ItemKind || !StringComparer.Ordinal.Equals(grid.RowNodeId, view.ItemNodeId) ||
                 !BaseStudioSha256.FixedTimeEquals(grid.RowNodeChecksum, view.ItemNodeChecksum)))
                throw new ArgumentException("A Studio grid differs from its owning view item contract.", nameof(views));
            if (view.Presentation.Chart is { } chart)
            {
                BaseStudioViewRegistration? bucket = views.SingleOrDefault(value => StringComparer.Ordinal.Equals(value.ViewId, chart.BucketViewId));
                BaseStudioViewRegistration? table = views.SingleOrDefault(value => StringComparer.Ordinal.Equals(value.ViewId, chart.EquivalentTableViewId));
                if (bucket is null || table is null ||
                    !BaseStudioSha256.FixedTimeEquals(bucket.DisclosureChannelChecksum, chart.DisclosureChannelChecksum) ||
                    !BaseStudioSha256.FixedTimeEquals(table.DisclosureChannelChecksum, chart.DisclosureChannelChecksum) ||
                    !EquivalentFilters(bucket.Filters, table.Filters))
                    throw new ArgumentException("A Studio chart is not bound to equivalent bucket and table views.", nameof(views));
            }
        }
        foreach (BaseStudioPageRegistration page in pages)
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
                foreach (BaseStudioResourceKind kind in detail.AcceptedKinds)
                {
                    if (!page.AcceptedResources.Contains(kind) || !detail.DetailPageIds.Any(id =>
                            pages.Single(value => StringComparer.Ordinal.Equals(value.PageId, id)).AcceptedResources.Contains(kind)))
                        throw new ArgumentException("Studio contextual-detail authority differs from its page/resource graph.", nameof(pages));
                }
            }
        }
        if (moduleClass == BaseStudioModuleClass.Framework && pages.Any(static page => page.Presentation.NavigationRole == BaseStudioNavigationRole.AreaLanding))
            throw new ArgumentException("Framework Studio modules cannot own task-area landing pages.", nameof(pages));
        foreach (BaseStudioArea area in moduleClass == BaseStudioModuleClass.Base ? pages.Select(static value => value.Area).Distinct() : [])
        {
            if (pages.Count(page => page.Area == area && page.Presentation.NavigationRole == BaseStudioNavigationRole.AreaLanding) != 1)
                throw new ArgumentException("Every disclosed Studio area requires exactly one landing page.", nameof(pages));
        }
    }

    private static bool EquivalentFilters(ImmutableArray<BaseStudioFilterRegistration> left,
        ImmutableArray<BaseStudioFilterRegistration> right)
        => left.Length == right.Length && left.Zip(right).All(static pair =>
            StringComparer.Ordinal.Equals(pair.First.FilterId, pair.Second.FilterId) &&
            BaseStudioSha256.FixedTimeEquals(pair.First.Checksum, pair.Second.Checksum));

    internal static bool HasRouteOverlap(IEnumerable<BaseStudioPageRegistration> pages)
    {
        BaseStudioPageRegistration[] values = pages.ToArray();
        for (int left = 0; left < values.Length; left++)
            for (int right = left + 1; right < values.Length; right++)
                if (values[left].Route.Overlaps(values[right].Route)) return true;
        return false;
    }
}
