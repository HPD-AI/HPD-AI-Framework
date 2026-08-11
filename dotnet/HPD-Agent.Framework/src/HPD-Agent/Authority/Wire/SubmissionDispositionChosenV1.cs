using System.Formats.Cbor;

namespace HPD.Agent.Authority;

/// <summary>Describes the closed S1-owned S4.L2 disposition.</summary>
public enum SubmissionDispositionV1 : ushort
{
    /// <summary>The reservation became the sole submission claim eligible for Agent-core admission.</summary>
    SubmissionClaimed = 1,
    /// <summary>Withdrawal won before submission and created a durable tombstone.</summary>
    WithdrawalTombstoned = 2,
    /// <summary>An immutable recovery observation reports that a different reservation fingerprint already owns the key.</summary>
    ReservationConflict = 3,
}

/// <summary>Records the S1-owned S4.L2 outcome that precedes any Agent-core semantic acceptance.</summary>
public sealed class SubmissionDispositionChosenV1 : IEquatable<SubmissionDispositionChosenV1>
{
    /// <summary>Initializes a validated S4.L2 disposition fact.</summary>
    /// <param name="operationId">The stable semantic handoff operation.</param>
    /// <param name="sourcePosition">The exact admitted <c>SemanticReservationCreatedV1</c> predecessor position.</param>
    /// <param name="authority">The exact relevant authority vector pinned by the disposition.</param>
    /// <param name="disposition">The closed S4.L2 outcome.</param>
    /// <exception cref="ArgumentNullException"><paramref name="authority"/> is null.</exception>
    /// <exception cref="ArgumentException">An identity, position, authority vector, or disposition is invalid or session-mismatched.</exception>
    public SubmissionDispositionChosenV1(
        OperationId operationId,
        JournalPositionV1 sourcePosition,
        ExpectedAuthorityVectorV1 authority,
        SubmissionDispositionV1 disposition)
    {
        if (!operationId.IsValid) throw new ArgumentException("An operation identity is required.", nameof(operationId));
        if (!sourcePosition.IsValid) throw new ArgumentException("A source position is required.", nameof(sourcePosition));
        ArgumentNullException.ThrowIfNull(authority);
        if (authority.Session != sourcePosition.Session)
            throw new ArgumentException("Source and authority sessions must match.", nameof(authority));
        if (!Enum.IsDefined(disposition)) throw new ArgumentException("The disposition is outside the closed registry.", nameof(disposition));
        OperationId = operationId;
        SourcePosition = sourcePosition;
        Authority = authority;
        Disposition = disposition;
    }

    /// <summary>Gets the stable semantic handoff operation.</summary>
    public OperationId OperationId { get; }
    /// <summary>Gets the exact admitted <c>SemanticReservationCreatedV1</c> predecessor position.</summary>
    public JournalPositionV1 SourcePosition { get; }
    /// <summary>Gets the authority vector pinned by the disposition.</summary>
    public ExpectedAuthorityVectorV1 Authority { get; }
    /// <summary>Gets the closed S4.L2 disposition.</summary>
    public SubmissionDispositionV1 Disposition { get; }

    /// <inheritdoc />
    public bool Equals(SubmissionDispositionChosenV1? other) => other is not null && OperationId == other.OperationId &&
        SourcePosition == other.SourcePosition && Authority == other.Authority && Disposition == other.Disposition;
    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is SubmissionDispositionChosenV1 other && Equals(other);
    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(OperationId, SourcePosition, Authority, Disposition);
    /// <summary>Returns whether two facts contain the same S4.L2 disposition evidence.</summary>
    public static bool operator ==(SubmissionDispositionChosenV1? left, SubmissionDispositionChosenV1? right) =>
        ReferenceEquals(left, right) || left is not null && left.Equals(right);
    /// <summary>Returns whether two facts contain different S4.L2 disposition evidence.</summary>
    public static bool operator !=(SubmissionDispositionChosenV1? left, SubmissionDispositionChosenV1? right) => !(left == right);
}

internal static class SubmissionDispositionChosenV1Codec
{
    internal const string SchemaId = "hpd.submission-disposition-chosen.v1";
    internal const ushort Major = 1;
    internal const ushort Minor = 0;

    internal static byte[] Encode(SubmissionDispositionChosenV1 value)
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

    internal static bool TryDecode(ReadOnlyMemory<byte> encoded, out SubmissionDispositionChosenV1? value)
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
            var rawDisposition = reader.ReadUInt64();
            reader.ReadEndMap();
            if (reader.BytesRemaining != 0 || rawDisposition is 0 or > 3) return false;
            value = new SubmissionDispositionChosenV1(
                OperationId.FromValue(StableId128.FromBytes(operation)), source, authority,
                (SubmissionDispositionV1)rawDisposition);
            return true;
        }
        catch (Exception exception) when (exception is CborContentException or InvalidOperationException or ArgumentException or OverflowException)
        {
            value = null;
            return false;
        }
    }

    internal static Hash256 ComputeIntegrityHash(SubmissionDispositionChosenV1 value) =>
        AuthorityIntegrityHashV1.Compute(SchemaId, Major, Minor, Encode(value));
}
