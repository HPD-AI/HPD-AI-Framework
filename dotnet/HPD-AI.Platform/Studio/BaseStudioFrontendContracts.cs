using System.Collections.Immutable;

namespace HPD.AI.Platform.Studio;

/// <summary>Defines one client slot supported by a static Studio module asset.</summary>
public sealed class BaseStudioFrontendClientSlot
{
    private BaseStudioFrontendClientSlot(string id, int version, BaseStudioFrameworkClientProtocol protocol,
        BaseStudioSha256 runtimeAbi, BaseStudioSha256 contract, BaseStudioSha256 operations, string endpointSurface,
        BaseStudioFrameworkClientTransportClass transport, ImmutableArray<string> pages, BaseStudioFrameworkClientLimits limits,
        BaseStudioSha256 checksum)
    { ClientId = id; Version = version; Protocol = protocol; StaticRuntimeAbiChecksum = runtimeAbi;
      GeneratedContractChecksum = contract; OperationInventoryChecksum = operations; EndpointSurfaceId = endpointSurface;
      TransportClass = transport; OwningPageIds = pages; Limits = limits; Checksum = checksum; }
    /// <summary>Gets the client-slot identity.</summary>
    public string ClientId { get; }
    /// <summary>Gets the client-slot version.</summary>
    public int Version { get; }
    /// <summary>Gets the closed generated-client protocol.</summary>
    public BaseStudioFrameworkClientProtocol Protocol { get; }
    /// <summary>Gets the static runtime interpreter ABI checksum.</summary>
    public BaseStudioSha256 StaticRuntimeAbiChecksum { get; }
    /// <summary>Gets the generated contract checksum.</summary>
    public BaseStudioSha256 GeneratedContractChecksum { get; }
    /// <summary>Gets the operation-inventory checksum.</summary>
    public BaseStudioSha256 OperationInventoryChecksum { get; }
    /// <summary>Gets the installed endpoint surface.</summary>
    public string EndpointSurfaceId { get; }
    /// <summary>Gets the transport class.</summary>
    public BaseStudioFrameworkClientTransportClass TransportClass { get; }
    /// <summary>Gets the canonical owning pages.</summary>
    public ImmutableArray<string> OwningPageIds { get; }
    /// <summary>Gets the bounded client limits.</summary>
    public BaseStudioFrameworkClientLimits Limits { get; }
    /// <summary>Gets the canonical slot checksum.</summary>
    public BaseStudioSha256 Checksum { get; }
    /// <summary>Creates one static frontend client slot.</summary>
    public static BaseStudioFrontendClientSlot Create(string id, int version, BaseStudioFrameworkClientProtocol protocol,
        BaseStudioSha256 runtimeAbi, BaseStudioSha256 contract, BaseStudioSha256 operations, string endpointSurface,
        BaseStudioFrameworkClientTransportClass transport, IEnumerable<string> owningPages, BaseStudioFrameworkClientLimits limits)
    {
        StudioContractValidation.Id(id); StudioContractValidation.Enum(protocol); StudioContractValidation.Id(endpointSurface);
        StudioContractValidation.Enum(transport); ArgumentNullException.ThrowIfNull(runtimeAbi); ArgumentNullException.ThrowIfNull(contract);
        ArgumentNullException.ThrowIfNull(operations); ArgumentNullException.ThrowIfNull(limits);
        if (version < 1) throw new ArgumentOutOfRangeException(nameof(version));
        BaseStudioSha256 owned = BaseStudioSha256.FromBytes(runtimeAbi.ToArray());
        BaseStudioSha256 ownedContract = BaseStudioSha256.FromBytes(contract.ToArray());
        BaseStudioSha256 ownedOperations = BaseStudioSha256.FromBytes(operations.ToArray());
        ImmutableArray<string> pages = StudioContractValidation.Ids(owningPages, 64, false, nameof(owningPages));
        BaseStudioSha256 checksum = StudioCanonicalEncoding.Hash("base.studio.frontend-client-slot.v1", writer =>
        { writer.String(id); writer.Int32(version); writer.Enum(protocol); writer.Checksum(owned); writer.Checksum(ownedContract);
          writer.Checksum(ownedOperations); writer.String(endpointSurface); writer.Enum(transport); writer.Count(pages.Length);
          foreach (string page in pages) writer.String(page); writer.Checksum(limits.Checksum); });
        return new(id, version, protocol, owned, ownedContract, ownedOperations, endpointSurface, transport, pages, limits, checksum);
    }
}

/// <summary>Binds one registered page to a static Svelte component export.</summary>
public sealed class BaseStudioPageComponentBinding
{
    private BaseStudioPageComponentBinding(string page, string export, BaseStudioSha256 componentAbi, BaseStudioSha256 checksum)
    { PageId = page; ComponentExportId = export; ComponentAbiChecksum = componentAbi; Checksum = checksum; }
    /// <summary>Gets the registered page identity.</summary>
    public string PageId { get; }
    /// <summary>Gets the static component export identity.</summary>
    public string ComponentExportId { get; }
    /// <summary>Gets the pinned Svelte page-props ABI checksum.</summary>
    public BaseStudioSha256 ComponentAbiChecksum { get; }
    /// <summary>Gets the canonical binding checksum.</summary>
    public BaseStudioSha256 Checksum { get; }
    /// <summary>Creates one static page-component binding.</summary>
    public static BaseStudioPageComponentBinding Create(string page, string export, BaseStudioSha256 componentAbi)
    {
        StudioContractValidation.Id(page); StudioContractValidation.Id(export); ArgumentNullException.ThrowIfNull(componentAbi);
        BaseStudioSha256 owned = BaseStudioSha256.FromBytes(componentAbi.ToArray());
        BaseStudioSha256 checksum = StudioCanonicalEncoding.Hash("base.studio.page-component.v1", writer =>
        { writer.String(page); writer.String(export); writer.Checksum(owned); });
        return new(page, export, owned, checksum);
    }
}

/// <summary>Defines the authorization-neutral static frontend ABI for one module chunk.</summary>
public sealed class BaseStudioFrontendExport
{
    private BaseStudioFrontendExport(string module, int version, ImmutableArray<BaseStudioFrontendClientSlot> clients,
        ImmutableArray<BaseStudioPageComponentBinding> components, BaseStudioSha256 checksum)
    { ModuleId = module; ModuleVersion = version; ClientSlots = clients; Components = components; FrontendAbiChecksum = checksum; }
    /// <summary>Gets the static module identity.</summary>
    public string ModuleId { get; }
    /// <summary>Gets the static module version.</summary>
    public int ModuleVersion { get; }
    /// <summary>Gets client slots in canonical identity/version order.</summary>
    public ImmutableArray<BaseStudioFrontendClientSlot> ClientSlots { get; }
    /// <summary>Gets page-component bindings in ordinal page order.</summary>
    public ImmutableArray<BaseStudioPageComponentBinding> Components { get; }
    /// <summary>Gets the static frontend ABI checksum.</summary>
    public BaseStudioSha256 FrontendAbiChecksum { get; }

    /// <summary>Creates and checksums one static frontend ABI descriptor.</summary>
    public static BaseStudioFrontendExport Create(string module, int version,
        IEnumerable<BaseStudioFrontendClientSlot> clients, IEnumerable<BaseStudioPageComponentBinding> components)
    {
        StudioContractValidation.Id(module); if (version < 1) throw new ArgumentOutOfRangeException(nameof(version));
        ImmutableArray<BaseStudioFrontendClientSlot> ownedClients = StudioGraphValidation.Ordered(
            clients, 32, static value => (value.ClientId, value.Version), nameof(clients));
        ImmutableArray<BaseStudioPageComponentBinding> ownedComponents = StudioGraphValidation.OrderedIdentity(
            components, 64, static value => value.PageId, nameof(components));
        if (ownedComponents.Select(static value => value.ComponentExportId).Distinct(StringComparer.Ordinal).Count() != ownedComponents.Length)
            throw new ArgumentException("Studio component exports must be unique.", nameof(components));
        BaseStudioSha256 checksum = StudioCanonicalEncoding.Hash("base.studio.frontend-abi.v1", writer =>
        {
            writer.String(module); writer.Int32(version);
            StudioGraphValidation.Encode(writer, ownedClients, static value => value.Checksum);
            StudioGraphValidation.Encode(writer, ownedComponents, static value => value.Checksum);
        });
        return new(module, version, ownedClients, ownedComponents, checksum);
    }

    internal void RequireCorrespondence(BaseStudioModuleRegistration registration)
    {
        if (!StringComparer.Ordinal.Equals(ModuleId, registration.Identity.ModuleId) || ModuleVersion != registration.Identity.Version ||
            Components.Length != registration.Pages.Length || !Components.Select(static value => value.PageId).SequenceEqual(registration.Pages.Select(static value => value.PageId)) ||
            ClientSlots.Length != registration.Clients.Length || ClientSlots.Zip(registration.Clients).Any(static pair =>
                !StringComparer.Ordinal.Equals(pair.First.ClientId, pair.Second.ClientId) || pair.First.Version != pair.Second.Version ||
                pair.First.Protocol != pair.Second.Protocol ||
                !BaseStudioSha256.FixedTimeEquals(pair.First.StaticRuntimeAbiChecksum, pair.Second.StaticRuntimeAbiChecksum) ||
                !BaseStudioSha256.FixedTimeEquals(pair.First.GeneratedContractChecksum, pair.Second.GeneratedContractChecksum) ||
                !BaseStudioSha256.FixedTimeEquals(pair.First.OperationInventoryChecksum, pair.Second.OperationInventoryChecksum) ||
                !StringComparer.Ordinal.Equals(pair.First.EndpointSurfaceId, pair.Second.EndpointSurfaceId) ||
                pair.First.TransportClass != pair.Second.TransportClass ||
                !pair.First.OwningPageIds.SequenceEqual(pair.Second.OwningPageIds) ||
                !BaseStudioSha256.FixedTimeEquals(pair.First.Limits.Checksum, pair.Second.Limits.Checksum)))
            throw new InvalidOperationException("The static Studio frontend ABI differs from the installed module registration.");
    }
}
