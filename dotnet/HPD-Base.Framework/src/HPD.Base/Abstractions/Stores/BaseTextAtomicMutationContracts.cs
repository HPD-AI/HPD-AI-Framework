using System.Collections.Immutable;

namespace HPD.Base;

/// <summary>Contains Runtime-owned canonical text projection intent for one atomic mutation.</summary>
public sealed record BaseFinalizedTextMutationExtension
{
    /// <summary>Gets facts ordered by mutation ordinal, index identity.</summary>
    public required ImmutableArray<BaseTextProjectionFact> Facts { get; init; }
    /// <summary>Gets the canonical digest of every fact.</summary>
    public required ImmutableArray<byte> ProjectionDigest { get; init; }
}

/// <summary>Classifies one text carrier transition.</summary>
public enum BaseTextProjectionDisposition
{
    /// <summary>Creates or replaces one current carrier.</summary>
    Upsert = 0,
    /// <summary>Removes one current carrier.</summary>
    Remove = 1,
}

/// <summary>Contains one canonical stable-field carrier value.</summary>
public sealed record BaseTextProjectionFieldValue
{
    /// <summary>Gets the stable L44 field identity.</summary>
    public required string StableFieldId { get; init; }
    /// <summary>Gets canonical JSON UTF-8, or empty for a missing field.</summary>
    public required ImmutableArray<byte> CanonicalJsonUtf8 { get; init; }
    /// <summary>Gets whether the field is absent rather than present-null.</summary>
    public required bool Missing { get; init; }
}

/// <summary>Contains one frozen record state in text projection authority.</summary>
public sealed record BaseTextProjectionRecordState
{
    /// <summary>Gets the opaque revision. Finalized after intent leaves this null until apply.</summary>
    public RevisionToken? Revision { get; init; }
    /// <summary>Gets canonical searchable and filter carrier fields.</summary>
    public required ImmutableArray<BaseTextProjectionFieldValue> Fields { get; init; }
    /// <summary>Gets the tenant scope carried by the mutation when present.</summary>
    public string? TenantId { get; init; }
    /// <summary>Gets the project scope carried by the mutation when present.</summary>
    public string? ProjectId { get; init; }
    /// <summary>Gets the canonical state checksum.</summary>
    public required ImmutableArray<byte> StateChecksum { get; init; }
}

/// <summary>Contains one Runtime-owned carrier transition for one installed index.</summary>
public sealed record BaseTextProjectionFact
{
    /// <summary>Gets the source mutation ordinal.</summary>
    public required int MutationOrdinal { get; init; }
    /// <summary>Gets the collection identity.</summary>
    public required string CollectionId { get; init; }
    /// <summary>Gets the installed text-index identity and version.</summary>
    public required string TextIndexId { get; init; }
    /// <summary>Gets the installed text-index version.</summary>
    public required int TextIndexVersion { get; init; }
    /// <summary>Gets the installed index checksum.</summary>
    public required ImmutableArray<byte> TextIndexChecksum { get; init; }
    /// <summary>Gets the record identity.</summary>
    public required RecordId RecordId { get; init; }
    /// <summary>Gets the exact prior state when present.</summary>
    public BaseTextProjectionRecordState? Before { get; init; }
    /// <summary>Gets the resulting semantic state when present.</summary>
    public BaseTextProjectionRecordState? After { get; init; }
    /// <summary>Gets the carrier disposition.</summary>
    public required BaseTextProjectionDisposition Disposition { get; init; }
    /// <summary>Gets the canonical fact checksum.</summary>
    public required ImmutableArray<byte> FactChecksum { get; init; }
}

/// <summary>Contains provider preparation evidence for finalized text projection intent.</summary>
public sealed record BasePreparedTextMutationEvidence
{
    /// <summary>Gets the exact finalized projection digest.</summary>
    public required ImmutableArray<byte> ProjectionDigest { get; init; }
    /// <summary>Gets the prepared facts count.</summary>
    public required int Facts { get; init; }
    /// <summary>Gets one captured generation for every affected index.</summary>
    public required ImmutableArray<BasePreparedTextIndexEvidence> Indexes { get; init; }
    /// <summary>Gets exact retained evidence bytes.</summary>
    public required long EvidenceBytes { get; init; }
}

/// <summary>Binds preparation to one exact installed text-index generation.</summary>
public sealed record BasePreparedTextIndexEvidence
{
    /// <summary>Gets the collection identity.</summary>
    public required string CollectionId { get; init; }
    /// <summary>Gets the text-index identity and version.</summary>
    public required string TextIndexId { get; init; }
    /// <summary>Gets the text-index version.</summary>
    public required int TextIndexVersion { get; init; }
    /// <summary>Gets the transaction-captured generation.</summary>
    public required long CapturedGeneration { get; init; }
    /// <summary>Gets the installed index checksum.</summary>
    public required ImmutableArray<byte> TextIndexChecksum { get; init; }
}

/// <summary>Contains revision-bearing provisional text projection evidence.</summary>
public sealed record BaseAppliedTextMutationEvidence
{
    /// <summary>Gets applied facts in finalized order.</summary>
    public required ImmutableArray<BaseTextProjectionFact> Facts { get; init; }
    /// <summary>Gets the same index-generation bindings accepted during preparation.</summary>
    public required ImmutableArray<BasePreparedTextIndexEvidence> Indexes { get; init; }
    /// <summary>Gets the canonical applied digest.</summary>
    public required ImmutableArray<byte> EvidenceDigest { get; init; }
    /// <summary>Gets exact retained evidence bytes.</summary>
    public required long EvidenceBytes { get; init; }
}
