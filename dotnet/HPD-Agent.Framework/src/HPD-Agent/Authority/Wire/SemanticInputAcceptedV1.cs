using System.Formats.Cbor;

namespace HPD.Agent.Authority;

/// <summary>Describes the sole positive disposition of an admitted semantic-input fact.</summary>
public enum SemanticInputAcceptanceDispositionV1 : ushort
{
    /// <summary>Agent core durably accepted the source decision at S4.L3.</summary>
    Accepted = 1,
}

/// <summary>Binds one finalized source decision to the sole Agent-core semantic acceptance point.</summary>
/// <remarks>This fact is authority evidence only after admission by the bound journal at S1.P0.</remarks>
public sealed class SemanticInputAcceptedV1 : IEquatable<SemanticInputAcceptedV1>
{
    /// <summary>Initializes a validated semantic-input acceptance fact.</summary>
    /// <param name="operationId">The stable semantic handoff operation.</param>
    /// <param name="sourcePosition">The admitted source decision position.</param>
    /// <param name="authority">The exact relevant authority vector revalidated at acceptance.</param>
    /// <param name="disposition">The closed positive acceptance disposition.</param>
    /// <exception cref="ArgumentNullException"><paramref name="authority"/> is null.</exception>
    /// <exception cref="ArgumentException">An identity, position, authority vector, or disposition is invalid or session-mismatched.</exception>
    public SemanticInputAcceptedV1(
        OperationId operationId,
        JournalPositionV1 sourcePosition,
        ExpectedAuthorityVectorV1 authority,
        SemanticInputAcceptanceDispositionV1 disposition)
    {
        if (!operationId.IsValid) throw new ArgumentException("An operation identity is required.", nameof(operationId));
        if (!sourcePosition.IsValid) throw new ArgumentException("A source position is required.", nameof(sourcePosition));
        ArgumentNullException.ThrowIfNull(authority);
        if (authority.Session != sourcePosition.Session) throw new ArgumentException("Source and authority sessions must match.", nameof(authority));
        if (!Enum.IsDefined(disposition)) throw new ArgumentException("The disposition is outside the closed registry.", nameof(disposition));
        OperationId = operationId;
        SourcePosition = sourcePosition;
        Authority = authority;
        Disposition = disposition;
    }

    /// <summary>Gets the stable semantic handoff operation.</summary>
    public OperationId OperationId { get; }
    /// <summary>Gets the admitted source decision position.</summary>
    public JournalPositionV1 SourcePosition { get; }
    /// <summary>Gets the authority vector revalidated at acceptance.</summary>
    public ExpectedAuthorityVectorV1 Authority { get; }
    /// <summary>Gets the closed positive acceptance disposition.</summary>
    public SemanticInputAcceptanceDispositionV1 Disposition { get; }

    /// <inheritdoc />
    public bool Equals(SemanticInputAcceptedV1? other) => other is not null && OperationId == other.OperationId &&
        SourcePosition == other.SourcePosition && Authority == other.Authority && Disposition == other.Disposition;
    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is SemanticInputAcceptedV1 other && Equals(other);
    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(OperationId, SourcePosition, Authority, Disposition);

    /// <summary>Returns whether two facts contain the same semantic acceptance evidence.</summary>
    public static bool operator ==(SemanticInputAcceptedV1? left, SemanticInputAcceptedV1? right) =>
        ReferenceEquals(left, right) || left is not null && left.Equals(right);

    /// <summary>Returns whether two facts contain different semantic acceptance evidence.</summary>
    public static bool operator !=(SemanticInputAcceptedV1? left, SemanticInputAcceptedV1? right) => !(left == right);
}

internal static class SemanticInputAcceptedV1Codec
{
    internal const string SchemaId = "hpd.semantic-input-accepted.v1";
    internal const ushort Major = 1;
    internal const ushort Minor = 0;

    internal static byte[] Encode(SemanticInputAcceptedV1 value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var writer = new CborWriter(CborConformanceMode.Ctap2Canonical);
        Span<byte> operation = stackalloc byte[16];
        if (!value.OperationId.TryWriteBytes(operation)) throw new ArgumentException("The operation identity is invalid.", nameof(value));
        writer.WriteStartMap(4);
        writer.WriteUInt64(1); writer.WriteByteString(operation);
        writer.WriteUInt64(2); AuthorityPositionCodecsV1.Write(writer, value.SourcePosition);
        writer.WriteUInt64(3); AuthorityVectorCodecsV1.WriteVector(writer, value.Authority);
        writer.WriteUInt64(4); writer.WriteUInt64((ushort)value.Disposition);
        writer.WriteEndMap();
        return writer.Encode();
    }

    internal static bool TryDecode(ReadOnlyMemory<byte> encoded, out SemanticInputAcceptedV1? value)
    {
        value = null;
        try
        {
            var reader = new CborReader(encoded, CborConformanceMode.Ctap2Canonical, false);
            if (reader.ReadStartMap() != 4 || reader.ReadUInt64() != 1) return false;
            Span<byte> operation = stackalloc byte[16];
            if (!reader.TryReadByteString(operation, out var written) || written != 16 || reader.ReadUInt64() != 2) return false;
            var source = AuthorityPositionCodecsV1.ReadJournal(reader);
            if (reader.ReadUInt64() != 3) return false;
            var authority = AuthorityVectorCodecsV1.ReadVector(reader);
            if (reader.ReadUInt64() != 4) return false;
            var disposition = reader.ReadUInt64();
            reader.ReadEndMap();
            if (reader.BytesRemaining != 0 || disposition != (ushort)SemanticInputAcceptanceDispositionV1.Accepted) return false;
            value = new SemanticInputAcceptedV1(
                OperationId.FromValue(StableId128.FromBytes(operation)), source, authority,
                SemanticInputAcceptanceDispositionV1.Accepted);
            return true;
        }
        catch (Exception exception) when (exception is CborContentException or InvalidOperationException or ArgumentException or OverflowException)
        {
            value = null;
            return false;
        }
    }

    internal static Hash256 ComputeIntegrityHash(SemanticInputAcceptedV1 value) =>
        AuthorityIntegrityHashV1.Compute(SchemaId, Major, Minor, Encode(value));
}
