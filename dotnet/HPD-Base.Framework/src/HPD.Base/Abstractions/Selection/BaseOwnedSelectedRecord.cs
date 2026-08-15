using System.Text.Json;

namespace HPD.Base;

/// <summary>
/// Owns one selected authoritative record as private canonical bytes across the
/// provider/Runtime trust boundary.
/// </summary>
public sealed class BaseOwnedSelectedRecord
{
    private readonly byte[] _canonicalBytes;

    private BaseOwnedSelectedRecord(RecordEnvelope record, int ordinal, int codecVersion, byte[] canonicalBytes)
    {
        RecordId = new string(record.Id.Value.AsSpan());
        Revision = record.Metadata.Revision
            ?? throw new ArgumentException("A selected record requires an authoritative revision.", nameof(record));
        SelectionOrdinal = ordinal;
        CodecVersion = codecVersion;
        _canonicalBytes = canonicalBytes;
    }

    /// <summary>Gets the stable selected record identifier.</summary>
    public string RecordId { get; }
    /// <summary>Gets the authoritative selected revision.</summary>
    public RevisionToken Revision { get; }
    /// <summary>Gets the contiguous zero-based selection ordinal.</summary>
    public int SelectionOrdinal { get; }
    /// <summary>Gets the canonical codec version.</summary>
    public int CodecVersion { get; }
    /// <summary>Gets the canonical byte count.</summary>
    public long CanonicalBytes => _canonicalBytes.LongLength;

    /// <summary>Validates and recursively freezes one provider record.</summary>
    public static BaseOwnedSelectedRecord Freeze(RecordEnvelope record, int selectionOrdinal, int codecVersion)
    {
        ArgumentNullException.ThrowIfNull(record);
        ArgumentOutOfRangeException.ThrowIfNegative(selectionOrdinal);
        ArgumentOutOfRangeException.ThrowIfLessThan(codecVersion, 1);
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(record, HPDBaseJsonSerializerContext.Default.RecordEnvelope);
        RecordEnvelope owned = JsonSerializer.Deserialize(bytes, HPDBaseJsonSerializerContext.Default.RecordEnvelope)
            ?? throw new ArgumentException("The selected record is not canonically serializable.", nameof(record));
        return new BaseOwnedSelectedRecord(owned, selectionOrdinal, codecVersion, bytes.ToArray());
    }

    /// <summary>Materializes a fresh recursively owned record graph.</summary>
    public RecordEnvelope MaterializeOwned() =>
        JsonSerializer.Deserialize(_canonicalBytes, HPDBaseJsonSerializerContext.Default.RecordEnvelope)
        ?? throw new InvalidOperationException("The owned selected record is invalid.");

    /// <summary>Returns a new copy of the canonical record bytes.</summary>
    public byte[] CopyCanonicalBytes() => _canonicalBytes.ToArray();
}
