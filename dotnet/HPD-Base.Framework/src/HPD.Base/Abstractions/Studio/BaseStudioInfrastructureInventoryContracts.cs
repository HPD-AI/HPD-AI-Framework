using System.Collections.Immutable;

namespace HPD.Base;

/// <summary>Identifies one closed provider-neutral infrastructure inventory.</summary>
public enum BaseStudioInfrastructureInventoryKind : byte
{
    /// <summary>Installed schema generations and drift facts.</summary>
    SchemaGeneration = 1,
    /// <summary>Registered and applied schema migrations.</summary>
    Migration = 2,
    /// <summary>Authenticated backup artifacts.</summary>
    Backup = 3,
    /// <summary>Identified restore attempts and outcomes.</summary>
    Restore = 4,
    /// <summary>Provider-owned bounded maintenance operations.</summary>
    Maintenance = 5,
}

/// <summary>Closed safe infrastructure lifecycle state.</summary>
public enum BaseStudioInfrastructureState : byte
{
    /// <summary>The item is planned.</summary>
    Planned = 1,
    /// <summary>The item is active.</summary>
    Active = 2,
    /// <summary>The item completed.</summary>
    Completed = 3,
    /// <summary>The item failed with protected details withheld.</summary>
    Failed = 4,
    /// <summary>The item is superseded by newer authority.</summary>
    Superseded = 5,
    /// <summary>The item requires reconciliation.</summary>
    Indeterminate = 6,
}

/// <summary>Contains independent provider ceilings for one infrastructure query.</summary>
public sealed record BaseStudioInfrastructureInventoryLimits
{
    /// <summary>Gets the maximum returned items.</summary>
    public required int MaximumItems { get; init; }
    /// <summary>Gets the maximum provider rows examined.</summary>
    public required long MaximumRowsRead { get; init; }
    /// <summary>Gets the maximum canonical result bytes.</summary>
    public required long MaximumEvidenceBytes { get; init; }
    /// <summary>Gets the maximum transient provider bytes.</summary>
    public required long MaximumTransientBytes { get; init; }
    /// <summary>Gets the authority-acquisition deadline.</summary>
    public required TimeSpan AcquisitionDeadline { get; init; }
    /// <summary>Gets the finite session lifetime.</summary>
    public required TimeSpan SessionDeadline { get; init; }
    /// <summary>Gets the page deadline.</summary>
    public required TimeSpan PageDeadline { get; init; }
}

/// <summary>Advertises dynamic-inventory support; static provider capabilities remain graph-owned.</summary>
public sealed record BaseStudioInfrastructureInventoryCapability
{
    /// <summary>Gets the canonically ordered supported kinds.</summary>
    public required ImmutableArray<BaseStudioInfrastructureInventoryKind> SupportedKinds { get; init; }
    /// <summary>Gets the provider maximum items per page.</summary>
    public required int MaximumItems { get; init; }
    /// <summary>Gets the provider maximum rows examined.</summary>
    public required long MaximumRowsRead { get; init; }
    /// <summary>Gets the provider maximum evidence bytes.</summary>
    public required long MaximumEvidenceBytes { get; init; }
    /// <summary>Gets the provider maximum transient bytes.</summary>
    public required long MaximumTransientBytes { get; init; }
    /// <summary>Gets the provider maximum acquisition deadline.</summary>
    public required TimeSpan AcquisitionDeadline { get; init; }
    /// <summary>Gets the provider maximum session lifetime.</summary>
    public required TimeSpan SessionDeadline { get; init; }
    /// <summary>Gets the provider maximum page deadline.</summary>
    public required TimeSpan PageDeadline { get; init; }
    /// <summary>Gets whether rows survive ordinary whole-store backup and restore.</summary>
    public required bool DurableThroughBackupRestore { get; init; }
    /// <summary>Gets the provider inventory certification checksum.</summary>
    public required ImmutableArray<byte> CertificationChecksum { get; init; }
}

/// <summary>Requests one exact inventory under current store authority.</summary>
public sealed record BaseStudioInfrastructureInventoryRequirement
{
    /// <summary>Gets the installed application ID.</summary>
    public required string ApplicationId { get; init; }
    /// <summary>Gets the configured store ID.</summary>
    public required string StoreId { get; init; }
    /// <summary>Gets the requested closed inventory.</summary>
    public required BaseStudioInfrastructureInventoryKind Kind { get; init; }
    /// <summary>Gets the expected coherent store instance.</summary>
    public required string StoreInstanceId { get; init; }
    /// <summary>Gets the expected restore epoch.</summary>
    public required long RestoreEpoch { get; init; }
    /// <summary>Gets the expected schema generation.</summary>
    public required long SchemaGeneration { get; init; }
    /// <summary>Gets the exact effective limits.</summary>
    public required BaseStudioInfrastructureInventoryLimits Limits { get; init; }
}

/// <summary>Contains provider-owned accounting for one inventory page.</summary>
public sealed record BaseStudioInfrastructureProviderAccounting
{
    /// <summary>Gets rows examined.</summary>
    public required long RowsRead { get; init; }
    /// <summary>Gets canonical evidence bytes.</summary>
    public required long EvidenceBytes { get; init; }
    /// <summary>Gets transient provider bytes.</summary>
    public required long TransientBytes { get; init; }
}

/// <summary>Contains the immutable dynamic authority captured by a provider.</summary>
public sealed record BaseStudioInfrastructureCaptureReceipt
{
    /// <summary>Gets the installed application ID.</summary>
    public required string ApplicationId { get; init; }
    /// <summary>Gets the configured store ID.</summary>
    public required string StoreId { get; init; }
    /// <summary>Gets the closed inventory.</summary>
    public required BaseStudioInfrastructureInventoryKind Kind { get; init; }
    /// <summary>Gets the coherent store instance.</summary>
    public required string StoreInstanceId { get; init; }
    /// <summary>Gets the captured restore epoch.</summary>
    public required long RestoreEpoch { get; init; }
    /// <summary>Gets the captured schema generation.</summary>
    public required long SchemaGeneration { get; init; }
    /// <summary>Gets the captured inventory generation.</summary>
    public required long InventoryGeneration { get; init; }
    /// <summary>Gets the fixed logical access-path ID.</summary>
    public required string LogicalAccessPathId { get; init; }
    /// <summary>Gets provider accounting for capture.</summary>
    public required BaseStudioInfrastructureProviderAccounting Accounting { get; init; }
    /// <summary>Gets the purpose-bound receipt checksum.</summary>
    public required ImmutableArray<byte> AuthorityChecksum { get; init; }
}

/// <summary>Provider-instance-bound, nonserializable inventory authority.</summary>
public abstract class BaseCapturedStudioInfrastructureAuthority
{
    /// <summary>Initializes a provider-owned authority.</summary>
    protected BaseCapturedStudioInfrastructureAuthority(BaseStudioInfrastructureCaptureReceipt receipt) => Receipt = receipt with
    { ApplicationId = new(receipt.ApplicationId.AsSpan()), StoreId = new(receipt.StoreId.AsSpan()), StoreInstanceId = new(receipt.StoreInstanceId.AsSpan()),
      LogicalAccessPathId = new(receipt.LogicalAccessPathId.AsSpan()), AuthorityChecksum = [.. receipt.AuthorityChecksum],
      Accounting = receipt.Accounting with { } };
    /// <summary>Gets the immutable capture receipt.</summary>
    public BaseStudioInfrastructureCaptureReceipt Receipt { get; }
}

/// <summary>Contains an exclusive canonical infrastructure boundary.</summary>
public sealed record BaseStudioInfrastructureBoundary
{
    /// <summary>Gets the inventory kind.</summary>
    public required BaseStudioInfrastructureInventoryKind Kind { get; init; }
    /// <summary>Gets the last returned positive sequence.</summary>
    public required long Sequence { get; init; }
    /// <summary>Gets the boundary checksum.</summary>
    public required ImmutableArray<byte> Checksum { get; init; }
}

/// <summary>Base of the closed provider-neutral infrastructure fact union.</summary>
public abstract record BaseStudioInfrastructureItem
{
    private protected BaseStudioInfrastructureItem() { }
    /// <summary>Gets the inventory kind.</summary>
    public required BaseStudioInfrastructureInventoryKind Kind { get; init; }
    /// <summary>Gets the positive canonical sequence.</summary>
    public required long Sequence { get; init; }
    /// <summary>Gets the configured store ID.</summary>
    public required string StoreId { get; init; }
    /// <summary>Gets the restore epoch owning this fact.</summary>
    public required long RestoreEpoch { get; init; }
    /// <summary>Gets the schema generation owning this fact.</summary>
    public required long SchemaGeneration { get; init; }
    /// <summary>Gets the canonical observed UTC.</summary>
    public required DateTimeOffset ObservedAtUtc { get; init; }
    /// <summary>Gets the safe closed state.</summary>
    public required BaseStudioInfrastructureState State { get; init; }
    /// <summary>Gets the fact checksum.</summary>
    public required ImmutableArray<byte> Checksum { get; init; }
}

/// <summary>Describes one exact schema generation.</summary>
public sealed record BaseStudioSchemaGenerationItem : BaseStudioInfrastructureItem
{
    /// <summary>Gets the provider-neutral schema baseline ID.</summary>
    public required string BaselineId { get; init; }
    /// <summary>Gets the schema checksum.</summary>
    public required ImmutableArray<byte> SchemaChecksum { get; init; }
    /// <summary>Gets whether provider drift was safely detected.</summary>
    public required bool DriftDetected { get; init; }
}

/// <summary>Describes one exact migration identity and progress fact.</summary>
public sealed record BaseStudioMigrationItem : BaseStudioInfrastructureItem
{
    /// <summary>Gets the migration ID.</summary>
    public required string MigrationId { get; init; }
    /// <summary>Gets the source schema generation.</summary>
    public required long FromSchemaGeneration { get; init; }
    /// <summary>Gets the target schema generation.</summary>
    public required long ToSchemaGeneration { get; init; }
    /// <summary>Gets the protected plan checksum.</summary>
    public required ImmutableArray<byte> PlanChecksum { get; init; }
}

/// <summary>Describes one authenticated backup artifact fact without artifact bytes.</summary>
public sealed record BaseStudioBackupItem : BaseStudioInfrastructureItem
{
    /// <summary>Gets the safe artifact identity.</summary>
    public required string ArtifactId { get; init; }
    /// <summary>Gets the authenticated artifact digest, or the all-zero no-artifact sentinel for a non-completed attempt.</summary>
    public required ImmutableArray<byte> ArtifactDigest { get; init; }
    /// <summary>Gets the authenticated artifact length, or zero when no completed artifact exists.</summary>
    public required long ArtifactBytes { get; init; }
}

/// <summary>Describes one identified restore fact.</summary>
public sealed record BaseStudioRestoreItem : BaseStudioInfrastructureItem
{
    /// <summary>Gets the restore request identity.</summary>
    public required string RestoreRequestIdentity { get; init; }
    /// <summary>Gets the source artifact digest, or the all-zero no-artifact sentinel when the attempt did not authenticate a source.</summary>
    public required ImmutableArray<byte> ArtifactDigest { get; init; }
    /// <summary>Gets the resulting restore epoch, or zero when no completed installation is known.</summary>
    public required long ResultRestoreEpoch { get; init; }
}

/// <summary>Describes one bounded provider maintenance fact.</summary>
public sealed record BaseStudioMaintenanceItem : BaseStudioInfrastructureItem
{
    /// <summary>Gets the closed maintenance kind.</summary>
    public required string MaintenanceKind { get; init; }
    /// <summary>Gets the operation identity.</summary>
    public required string OperationIdentity { get; init; }
    /// <summary>Gets safe progress basis points.</summary>
    public required int ProgressBasisPoints { get; init; }
}

/// <summary>Requests one finite page.</summary>
public sealed record BaseStudioInfrastructurePageRequest
{
    /// <summary>Gets the exclusive prior boundary.</summary>
    public BaseStudioInfrastructureBoundary? After { get; init; }
    /// <summary>Gets the requested positive item count.</summary>
    public required int Take { get; init; }
}

/// <summary>Returns one finite provider inventory page.</summary>
public sealed record BaseStudioInfrastructurePage
{
    /// <summary>Gets the canonically ordered facts.</summary>
    public required ImmutableArray<BaseStudioInfrastructureItem> Items { get; init; }
    /// <summary>Gets the next exclusive boundary when more facts exist.</summary>
    public BaseStudioInfrastructureBoundary? Next { get; init; }
    /// <summary>Gets the captured inventory generation.</summary>
    public required long InventoryGeneration { get; init; }
    /// <summary>Gets provider accounting.</summary>
    public required BaseStudioInfrastructureProviderAccounting Accounting { get; init; }
    /// <summary>Gets the page checksum.</summary>
    public required ImmutableArray<byte> PageChecksum { get; init; }
}

/// <summary>One provider-instance-bound, finite, single-owner inventory session.</summary>
public interface IBaseStudioInfrastructureInventorySession : IAsyncDisposable
{
    /// <summary>Reads the next finite page.</summary>
    ValueTask<OperationResult<BaseStudioInfrastructurePage>> ReadPageAsync(BaseStudioInfrastructurePageRequest request,
        CancellationToken cancellationToken = default);
}

/// <summary>Provider-neutral dynamic infrastructure inventory SPI.</summary>
public interface IBaseStudioInfrastructureInventoryStore
{
    /// <summary>Gets the provider dynamic-inventory capability.</summary>
    BaseStudioInfrastructureInventoryCapability InfrastructureInventoryCapability { get; }
    /// <summary>Captures coherent store/restore/schema and inventory authority.</summary>
    ValueTask<OperationResult<BaseCapturedStudioInfrastructureAuthority>> CaptureInfrastructureAuthorityAsync(
        BaseStudioInfrastructureInventoryRequirement request, CancellationToken cancellationToken = default);
    /// <summary>Opens the captured authority exactly once.</summary>
    ValueTask<OperationResult<IBaseStudioInfrastructureInventorySession>> OpenInfrastructureSessionAsync(
        BaseCapturedStudioInfrastructureAuthority authority, CancellationToken cancellationToken = default);
}
