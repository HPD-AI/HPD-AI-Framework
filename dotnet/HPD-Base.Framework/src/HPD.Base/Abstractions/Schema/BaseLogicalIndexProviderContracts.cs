using System.Collections.Immutable;

namespace HPD.Base;

/// <summary>Describes one provider's certified logical-index capability.</summary>
public sealed record BaseLogicalIndexProviderCapability
{
    /// <summary>Gets whether logical-index execution is supported.</summary>
    public required bool Supported { get; init; }
    /// <summary>Gets the equality-key codec version.</summary>
    public required int EqualityKeyCodecVersion { get; init; }
    /// <summary>Gets the exact supported access shapes.</summary>
    public required ImmutableArray<BaseIndexAccessShape> AccessShapes { get; init; }
    /// <summary>Gets the maximum installed indexes per collection.</summary>
    public required int MaximumIndexesPerCollection { get; init; }
    /// <summary>Gets the maximum parts in one index.</summary>
    public required int MaximumPartsPerIndex { get; init; }
    /// <summary>Gets the maximum predicate nodes in one index.</summary>
    public required int MaximumPredicateNodesPerIndex { get; init; }
    /// <summary>Gets the maximum canonical equality-key bytes.</summary>
    public required int MaximumCanonicalKeyBytes { get; init; }
    /// <summary>Gets the maximum indexed records in one collection.</summary>
    public required long MaximumIndexedRecordsPerCollection { get; init; }
    /// <summary>Gets the maximum postings in one index.</summary>
    public required long MaximumPostingsPerIndex { get; init; }
    /// <summary>Gets the maximum records in one posting.</summary>
    public required int MaximumPostingRecordsPerKey { get; init; }
    /// <summary>Gets the maximum postings retained by one store.</summary>
    public required long MaximumPostingsPerStore { get; init; }
    /// <summary>Gets the maximum canonical directory bytes in one index.</summary>
    public required long MaximumDirectoryBytesPerIndex { get; init; }
    /// <summary>Gets the maximum canonical directory bytes in one store.</summary>
    public required long MaximumDirectoryBytesPerStore { get; init; }
    /// <summary>Gets the maximum predicate evaluations in one directory publication.</summary>
    public required long MaximumDirectoryPredicateEvaluationsPerPublication { get; init; }
    /// <summary>Gets the maximum aggregate canonical key bytes in one index directory.</summary>
    public required long MaximumDirectoryKeyBytesPerIndex { get; init; }
    /// <summary>Gets the maximum transient directory work in one operation.</summary>
    public required long MaximumDirectoryTransientBytesPerOperation { get; init; }
    /// <summary>Gets the canonical capability checksum.</summary>
    public required ImmutableArray<byte> Checksum { get; init; }
}

/// <summary>Reports exact work observed for one logical-index certification case.</summary>
public sealed record BaseLogicalIndexCertificationAccounting
{
    /// <summary>Gets distinct records evaluated.</summary>
    public required long Records { get; init; }
    /// <summary>Gets predicate evaluations.</summary>
    public required long PredicateEvaluations { get; init; }
    /// <summary>Gets canonical keys produced.</summary>
    public required long Keys { get; init; }
    /// <summary>Gets canonical key bytes.</summary>
    public required long KeyBytes { get; init; }
    /// <summary>Gets retained posting keys.</summary>
    public required long PostingKeys { get; init; }
    /// <summary>Gets retained memberships.</summary>
    public required long Postings { get; init; }
    /// <summary>Gets retained comparator entries.</summary>
    public required long ComparatorEntries { get; init; }
    /// <summary>Gets comparator operations.</summary>
    public required long Comparisons { get; init; }
    /// <summary>Gets canonical evidence bytes.</summary>
    public required long EvidenceBytes { get; init; }
    /// <summary>Gets canonical retained directory bytes.</summary>
    public required long RetainedDirectoryBytes { get; init; }
    /// <summary>Gets peak canonical transient bytes.</summary>
    public required long TransientBytes { get; init; }
}

/// <summary>Reports one observed logical-index certification case.</summary>
public sealed record BaseLogicalIndexCertificationCaseResult
{
    /// <summary>Gets the stable case ID.</summary>
    public required string Id { get; init; }
    /// <summary>Gets the registry ordinal.</summary>
    public required int Ordinal { get; init; }
    /// <summary>Gets the observed operation status.</summary>
    public required OperationStatus ObservedStatus { get; init; }
    /// <summary>Gets the observed safe failure code.</summary>
    public string? ObservedErrorCode { get; init; }
    /// <summary>Gets exact case accounting.</summary>
    public required BaseLogicalIndexCertificationAccounting Accounting { get; init; }
    /// <summary>Gets the member-set checksum before execution.</summary>
    public required ImmutableArray<byte> BeforeMemberSetChecksum { get; init; }
    /// <summary>Gets the member-set checksum after execution.</summary>
    public required ImmutableArray<byte> AfterMemberSetChecksum { get; init; }
    /// <summary>Gets the directory-publication checksum before execution.</summary>
    public required ImmutableArray<byte> BeforePublicationChecksum { get; init; }
    /// <summary>Gets the directory-publication checksum after execution.</summary>
    public required ImmutableArray<byte> AfterPublicationChecksum { get; init; }
    /// <summary>Gets the canonical observed-evidence checksum.</summary>
    public required ImmutableArray<byte> EvidenceChecksum { get; init; }
}

/// <summary>Reports one complete provider logical-index certification run.</summary>
public sealed record BaseLogicalIndexCertificationReport
{
    /// <summary>Gets the stable provider ID.</summary>
    public required string ProviderId { get; init; }
    /// <summary>Gets the provider version.</summary>
    public required int ProviderVersion { get; init; }
    /// <summary>Gets the selected store-provider kind.</summary>
    public required string StoreProviderKind { get; init; }
    /// <summary>Gets the selected store-provider protocol version.</summary>
    public required int StoreProviderProtocolVersion { get; init; }
    /// <summary>Gets the production capability checksum.</summary>
    public required ImmutableArray<byte> ProductionCapabilityChecksum { get; init; }
    /// <summary>Gets the bounded certification capability checksum.</summary>
    public required ImmutableArray<byte> BoundedCertificationCapabilityChecksum { get; init; }
    /// <summary>Gets cases in immutable registry order.</summary>
    public required ImmutableArray<BaseLogicalIndexCertificationCaseResult> Cases { get; init; }
    /// <summary>Gets the certification-contract checksum.</summary>
    public required ImmutableArray<byte> ContractChecksum { get; init; }
    /// <summary>Gets the report checksum.</summary>
    public required ImmutableArray<byte> Checksum { get; init; }
}

/// <summary>Describes one frozen provider logical-index profile.</summary>
public sealed record BaseLogicalIndexProviderProfile
{
    /// <summary>Gets whether the profile is supported.</summary>
    public required bool Supported { get; init; }
    /// <summary>Gets the stable provider ID.</summary>
    public required string ProviderId { get; init; }
    /// <summary>Gets the provider version.</summary>
    public required int ProviderVersion { get; init; }
    /// <summary>Gets the selected store-provider kind.</summary>
    public required string StoreProviderKind { get; init; }
    /// <summary>Gets the selected store-provider protocol version.</summary>
    public required int StoreProviderProtocolVersion { get; init; }
    /// <summary>Gets the certified capability.</summary>
    public required BaseLogicalIndexProviderCapability Capability { get; init; }
    /// <summary>Gets sorted immutable native dependency receipts.</summary>
    public required ImmutableArray<string> NativeDependencyReceipts { get; init; }
    /// <summary>Gets the certification-contract checksum.</summary>
    public required ImmutableArray<byte> ContractChecksum { get; init; }
    /// <summary>Gets the executed-report checksum.</summary>
    public required ImmutableArray<byte> ExecutedReportChecksum { get; init; }
    /// <summary>Gets the profile checksum.</summary>
    public required ImmutableArray<byte> Checksum { get; init; }
}

/// <summary>Provides the closed, test-only observation seam used by provider certification.</summary>
internal interface IBaseLogicalIndexOperationalStore
{
    bool LogicalIndexesReady { get; }
}

/// <summary>Provides the closed, test-only observation seam used by provider certification.</summary>
internal interface IBaseLogicalIndexCertificationInspection : IBaseLogicalIndexOperationalStore
{
    BaseLogicalIndexProviderCapability LogicalIndexCertificationCapability { get; }

    ValueTask<BaseLogicalIndexCertificationSnapshot> InspectLogicalIndexForCertificationAsync(
        string collectionId,
        BaseLogicalIndexChecksum indexChecksum,
        CancellationToken cancellationToken = default);

    ValueTask CorruptLogicalIndexMemberSetForCertificationAsync(
        string collectionId,
        BaseLogicalIndexChecksum indexChecksum,
        CancellationToken cancellationToken = default);
}

/// <summary>Contains one deeply owned provider-private certification observation.</summary>
internal sealed record BaseLogicalIndexCertificationSnapshot
{
    internal required BaseLogicalIndexDirectoryAuthority Authority { get; init; }
    internal required BaseLogicalIndexDirectory Directory { get; init; }

    internal BaseLogicalIndexCertificationSnapshot DeepClone() => new()
    {
        Authority = Authority.DeepClone(),
        Directory = Directory.DeepClone(),
    };
}
