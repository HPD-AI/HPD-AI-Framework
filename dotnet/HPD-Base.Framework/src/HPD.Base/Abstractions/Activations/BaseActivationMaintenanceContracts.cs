using System.Collections.Immutable;

namespace HPD.Base;

/// <summary>Classifies one crash-recoverable activation maintenance operation.</summary>
public enum BaseActivationMaintenanceKind
{
    /// <summary>Returns expired claimed work to retry-pending authority.</summary>
    RecoverExpiredClaims,
    /// <summary>Moves effects whose exact executor authority is provably dead to outcome-unknown.</summary>
    RecoverExpiredEffects
}

/// <summary>Requests one identified bounded activation-maintenance page.</summary>
public sealed record BaseActivationMaintenanceRequest
{
    /// <summary>Gets the exact application identity.</summary>
    public required string ApplicationId { get; init; }
    /// <summary>Gets the exact protected scope seek.</summary>
    public required BaseOwnedScopeSeekAuthority Scope { get; init; }
    /// <summary>Gets the exact installed definition.</summary>
    public required BaseActivationDefinitionKey Definition { get; init; }
    /// <summary>Gets the closed maintenance kind.</summary>
    public required BaseActivationMaintenanceKind Kind { get; init; }
    /// <summary>Gets the optional exclusive activation-ID boundary.</summary>
    public string? AfterActivationId { get; init; }
    /// <summary>Gets the bounded page size.</summary>
    public required int Take { get; init; }
    /// <summary>Gets the provider-accepted time receipt.</summary>
    public required BaseAcceptedTimeReceipt AcceptedTime { get; init; }
    /// <summary>Gets the identified request authority.</summary>
    public required BaseMutationRequestIdentity Identity { get; init; }
    /// <summary>Gets the exact effective safety envelope.</summary>
    public required BaseActivationExecutionLimits Limits { get; init; }
}

/// <summary>Contains one committed activation-maintenance item.</summary>
public sealed record BaseActivationMaintenanceItem
{
    /// <summary>Gets the activation identity.</summary>
    public required string ActivationId { get; init; }
    /// <summary>Gets the prior generation.</summary>
    public required long PreviousGeneration { get; init; }
    /// <summary>Gets the committed generation.</summary>
    public required long ResultingGeneration { get; init; }
    /// <summary>Gets the prior state.</summary>
    public required BaseActivationState PreviousState { get; init; }
    /// <summary>Gets the committed state.</summary>
    public required BaseActivationState ResultingState { get; init; }
    /// <summary>Gets the committed control checksum.</summary>
    public required ImmutableArray<byte> ControlChecksum { get; init; }
}

/// <summary>Returns one crash-recoverable activation-maintenance page.</summary>
public sealed record BaseActivationMaintenancePage
{
    /// <summary>Gets committed items in activation-ID order.</summary>
    public required ImmutableArray<BaseActivationMaintenanceItem> Items { get; init; }
    /// <summary>Gets the next exclusive activation-ID boundary.</summary>
    public string? NextActivationId { get; init; }
    /// <summary>Gets whether the captured page reached its high-water.</summary>
    public required bool Completed { get; init; }
    /// <summary>Gets exact provider accounting.</summary>
    public required BaseActivationAccounting Accounting { get; init; }
    /// <summary>Gets request disposition.</summary>
    public required BaseMutationRequestDisposition Disposition { get; init; }
}

/// <summary>Requests pruning of dependency-free disposed activation authority.</summary>
public sealed record BaseActivationPruneRequest
{
    /// <summary>Gets the exact application identity.</summary>
    public required string ApplicationId { get; init; }
    /// <summary>Gets the exact protected scope seek.</summary>
    public required BaseOwnedScopeSeekAuthority Scope { get; init; }
    /// <summary>Gets the exact installed definition.</summary>
    public required BaseActivationDefinitionKey Definition { get; init; }
    /// <summary>Gets the optional exclusive activation-ID boundary.</summary>
    public string? AfterActivationId { get; init; }
    /// <summary>Gets the bounded page size.</summary>
    public required int Take { get; init; }
    /// <summary>Gets the provider-accepted time receipt.</summary>
    public required BaseAcceptedTimeReceipt AcceptedTime { get; init; }
    /// <summary>Gets the identified request authority.</summary>
    public required BaseMutationRequestIdentity Identity { get; init; }
    /// <summary>Gets the exact effective safety envelope.</summary>
    public required BaseActivationExecutionLimits Limits { get; init; }
}

/// <summary>Returns one committed activation-pruning page.</summary>
public sealed record BaseActivationPrunePage
{
    /// <summary>Gets the exact durable prune authority created for each removed activation.</summary>
    public required ImmutableArray<BaseActivationPruneEvidence> Items { get; init; }
    /// <summary>Gets the exact number of retained receipt payloads deleted by this page.</summary>
    public required int DeletedReceiptCount { get; init; }
    /// <summary>Gets the number of deleted receipts that consumed retained yield slots.</summary>
    public required int DeletedYieldReceiptCount { get; init; }
    /// <summary>Gets receipt-chain authority before pruning.</summary>
    public required BaseActivationInstanceReceiptChainState PriorChain { get; init; }
    /// <summary>Gets receipt-chain authority after pruning.</summary>
    public required BaseActivationInstanceReceiptChainState ResultingChain { get; init; }
    /// <summary>Gets yield-reservation authority before pruning.</summary>
    public required BaseActivationYieldReservationState PriorReservation { get; init; }
    /// <summary>Gets yield-reservation authority after pruning.</summary>
    public required BaseActivationYieldReservationState ResultingReservation { get; init; }
    /// <summary>Gets the next exclusive activation-ID boundary.</summary>
    public string? NextActivationId { get; init; }
    /// <summary>Gets whether the captured page reached its high-water.</summary>
    public required bool Completed { get; init; }
    /// <summary>Gets exact provider accounting.</summary>
    public required BaseActivationAccounting Accounting { get; init; }
    /// <summary>Gets request disposition.</summary>
    public required BaseMutationRequestDisposition Disposition { get; init; }
}

/// <summary>
/// Proves that one exact activation was dependency-free when L51 durably pruned it.
/// This non-prunable evidence is indexed by activation identity and may be consumed by
/// later BASE maintenance without reconstructing removed activation state.
/// </summary>
public sealed record BaseActivationPruneEvidence
{
    /// <summary>Gets the removed activation identity.</summary>
    public required string ActivationId { get; init; }
    /// <summary>Gets the exact installed activation definition.</summary>
    public required BaseActivationDefinitionKey Definition { get; init; }
    /// <summary>Gets the terminal activation generation.</summary>
    public required long TerminalGeneration { get; init; }
    /// <summary>Gets the exact terminal control checksum.</summary>
    public required ImmutableArray<byte> TerminalControlChecksum { get; init; }
    /// <summary>Gets the exact durable terminal-transition receipt checksum.</summary>
    public required ImmutableArray<byte> TerminalReceiptChecksum { get; init; }
    /// <summary>Gets the optional immutable schedule-occurrence checksum.</summary>
    public required ImmutableArray<byte>? OccurrenceChecksum { get; init; }
    /// <summary>Gets the optional canonical result checksum.</summary>
    public required ImmutableArray<byte>? ResultChecksum { get; init; }
    /// <summary>Gets the L51 authority generation published by the prune.</summary>
    public required long PruneAuthorityGeneration { get; init; }
    /// <summary>Gets the application owning the activation authority.</summary>
    public required string ApplicationId { get; init; }
    /// <summary>Gets the logical store identity.</summary>
    public required string LogicalStoreId { get; init; }
    /// <summary>Gets the physical store-instance identity.</summary>
    public required string StoreInstanceId { get; init; }
    /// <summary>Gets the restore epoch in which pruning committed.</summary>
    public required long RestoreEpoch { get; init; }
    /// <summary>Gets the exact L51 publication-authority checksum.</summary>
    public required ImmutableArray<byte> PublicationAuthorityChecksum { get; init; }
    /// <summary>Gets the purpose-bound canonical evidence checksum.</summary>
    public required ImmutableArray<byte> Checksum { get; init; }
}

/// <summary>Creates and validates canonical L51 per-activation prune evidence.</summary>
public static class BaseActivationPruneEvidenceContract
{
    private const int PurposeBytes = 30;
    /// <summary>Computes the exact purpose-bound evidence checksum.</summary>
    public static ImmutableArray<byte> Checksum(BaseActivationPruneEvidence value)
    {
        ArgumentNullException.ThrowIfNull(value);
        using var hash = System.Security.Cryptography.IncrementalHash.CreateHash(
            System.Security.Cryptography.HashAlgorithmName.SHA256);
        hash.AppendData("base.activation.pruneFloor.v1\0"u8);
        Add(System.Text.Encoding.UTF8.GetBytes(value.ActivationId));
        Add(System.Text.Encoding.UTF8.GetBytes(value.Definition.Id));
        Span<byte> number = stackalloc byte[8];
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(number[..4], value.Definition.Version);
        hash.AppendData(number[..4]); Add(value.Definition.Checksum.AsSpan());
        System.Buffers.Binary.BinaryPrimitives.WriteInt64BigEndian(number, value.TerminalGeneration); hash.AppendData(number);
        Add(value.TerminalControlChecksum.AsSpan()); Add(value.TerminalReceiptChecksum.AsSpan());
        Optional(value.OccurrenceChecksum); Optional(value.ResultChecksum);
        System.Buffers.Binary.BinaryPrimitives.WriteInt64BigEndian(number, value.PruneAuthorityGeneration); hash.AppendData(number);
        Add(System.Text.Encoding.UTF8.GetBytes(value.ApplicationId)); Add(System.Text.Encoding.UTF8.GetBytes(value.LogicalStoreId));
        Add(System.Text.Encoding.UTF8.GetBytes(value.StoreInstanceId));
        System.Buffers.Binary.BinaryPrimitives.WriteInt64BigEndian(number, value.RestoreEpoch); hash.AppendData(number);
        Add(value.PublicationAuthorityChecksum.AsSpan());
        return hash.GetHashAndReset().ToImmutableArray();

        void Add(ReadOnlySpan<byte> bytes)
        {
            Span<byte> length = stackalloc byte[4];
            System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(length, bytes.Length);
            hash.AppendData(length); hash.AppendData(bytes);
        }
        void Optional(ImmutableArray<byte>? bytes)
        {
            hash.AppendData(bytes is null ? [(byte)0] : [(byte)1]);
            if (bytes is { } present) Add(present.AsSpan());
        }
    }

    /// <summary>Returns whether evidence has the exact closed canonical shape and checksum.</summary>
    public static bool IsValid(BaseActivationPruneEvidence value) => value is not null
        && !string.IsNullOrWhiteSpace(value.ActivationId)
        && !string.IsNullOrWhiteSpace(value.Definition.Id) && value.Definition.Version > 0
        && value.Definition.Checksum.Length == 32 && value.TerminalGeneration > 0
        && value.TerminalControlChecksum.Length == 32 && value.TerminalReceiptChecksum.Length == 32
        && (value.OccurrenceChecksum is null || value.OccurrenceChecksum.Value.Length == 32)
        && (value.ResultChecksum is null || value.ResultChecksum.Value.Length == 32)
        && value.PruneAuthorityGeneration > 0 && !string.IsNullOrWhiteSpace(value.ApplicationId)
        && !string.IsNullOrWhiteSpace(value.LogicalStoreId) && !string.IsNullOrWhiteSpace(value.StoreInstanceId)
        && value.RestoreEpoch >= 0 && value.PublicationAuthorityChecksum.Length == 32 && value.Checksum.Length == 32
        && System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(
            Checksum(value with { Checksum = [] }).AsSpan(), value.Checksum.AsSpan());

    /// <summary>Measures the exact canonical bytes retained or returned for one evidence value.</summary>
    public static long MeasureCanonicalBytes(BaseActivationPruneEvidence value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return checked(PurposeBytes
            + Text(value.ActivationId) + Text(value.Definition.Id) + 4 + Bytes(value.Definition.Checksum)
            + 8 + Bytes(value.TerminalControlChecksum) + Bytes(value.TerminalReceiptChecksum)
            + Optional(value.OccurrenceChecksum) + Optional(value.ResultChecksum) + 8
            + Text(value.ApplicationId) + Text(value.LogicalStoreId) + Text(value.StoreInstanceId) + 8
            + Bytes(value.PublicationAuthorityChecksum) + Bytes(value.Checksum));
        static long Text(string text) => checked(4L + System.Text.Encoding.UTF8.GetByteCount(text));
        static long Bytes(ImmutableArray<byte> bytes) => checked(4L + bytes.Length);
        static long Optional(ImmutableArray<byte>? bytes) => bytes is null ? 1 : checked(1L + Bytes(bytes.Value));
    }
}

/// <summary>Requests exact outcome-unknown reconciliation under operator evidence.</summary>
public sealed record BaseActivationIndeterminateRequest
{
    /// <summary>Gets the closed reconciliation transition.</summary>
    public required BaseActivationReconcileEffectRequest Reconciliation { get; init; }
}

/// <summary>Returns exact indeterminate resolution evidence.</summary>
public sealed record BaseActivationIndeterminateResolution
{
    /// <summary>Gets the committed activation transition.</summary>
    public required BaseActivationTransitionResult Transition { get; init; }
}

/// <summary>Requests one bounded sanitized quarantine page.</summary>
public sealed record BaseActivationQuarantineRequest
{
    /// <summary>Gets an optional exclusive sequence boundary.</summary>
    public long? AfterSequence { get; init; }
    /// <summary>Gets the bounded page size.</summary>
    public required int Take { get; init; }
}

/// <summary>Contains one sanitized retained-work observation.</summary>
public sealed record BaseActivationQuarantineItem
{
    /// <summary>Gets the positive observation sequence.</summary>
    public required long Sequence { get; init; }
    /// <summary>Gets the stable operation kind.</summary>
    public required string Operation { get; init; }
    /// <summary>Gets the retention start instant.</summary>
    public required DateTimeOffset RetainedAt { get; init; }
}

/// <summary>Returns one sanitized quarantine page.</summary>
public sealed record BaseActivationQuarantinePage
{
    /// <summary>Gets retained work in sequence order.</summary>
    public required ImmutableArray<BaseActivationQuarantineItem> Items { get; init; }
    /// <summary>Gets the next exclusive sequence boundary.</summary>
    public long? NextSequence { get; init; }
}
