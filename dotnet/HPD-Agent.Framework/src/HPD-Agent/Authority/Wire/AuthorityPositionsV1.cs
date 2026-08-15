using System.Formats.Cbor;

namespace HPD.Agent.Authority;

/// <summary>Identifies one committed position in the sole session authority order.</summary>
public readonly record struct JournalPositionV1
{
    /// <summary>Initializes a validated journal position.</summary>
    /// <param name="session">The session authority stamp.</param>
    /// <param name="sequence">The positive contiguous session sequence.</param>
    /// <exception cref="ArgumentException">The session stamp is invalid.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The sequence is not positive.</exception>
    public JournalPositionV1(SessionAuthorityStampV1 session, long sequence)
    {
        if (!session.IsValid)
            throw new ArgumentException("A valid session authority stamp is required.", nameof(session));
        if (sequence <= 0)
            throw new ArgumentOutOfRangeException(nameof(sequence), "A committed journal sequence must be positive.");
        Session = session;
        Sequence = sequence;
    }

    /// <summary>Gets the session authority stamp.</summary>
    public SessionAuthorityStampV1 Session { get; }

    /// <summary>Gets the positive contiguous session sequence.</summary>
    public long Sequence { get; }

    /// <summary>Gets whether the position is valid at an authority boundary.</summary>
    public bool IsValid => Session.IsValid && Sequence > 0;
}

/// <summary>Identifies a secondary ordered position inside one thread generation.</summary>
/// <remarks>This position never orders facts belonging to different threads.</remarks>
public readonly record struct ThreadPositionV1
{
    /// <summary>Initializes a validated thread position.</summary>
    /// <param name="threadId">The thread owning the secondary position.</param>
    /// <param name="generation">The positive thread generation.</param>
    /// <param name="sequence">The positive sequence within the generation.</param>
    /// <exception cref="ArgumentException">The thread identifier is invalid.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The generation or sequence is not positive.</exception>
    public ThreadPositionV1(ThreadId threadId, long generation, long sequence)
    {
        if (!threadId.IsValid)
            throw new ArgumentException("A thread identifier is required.", nameof(threadId));
        if (generation <= 0)
            throw new ArgumentOutOfRangeException(nameof(generation), "A thread generation must be positive.");
        if (sequence <= 0)
            throw new ArgumentOutOfRangeException(nameof(sequence), "A committed thread sequence must be positive.");
        ThreadId = threadId;
        Generation = generation;
        Sequence = sequence;
    }

    /// <summary>Gets the thread identifier.</summary>
    public ThreadId ThreadId { get; }

    /// <summary>Gets the positive thread generation.</summary>
    public long Generation { get; }

    /// <summary>Gets the positive sequence within the thread generation.</summary>
    public long Sequence { get; }

    /// <summary>Gets whether all position components are valid.</summary>
    public bool IsValid => ThreadId.IsValid && Generation > 0 && Sequence > 0;
}

internal static class AuthorityPositionCodecsV1
{
    internal const string JournalSchemaId = "hpd.journal-position.v1";
    internal const string ThreadSchemaId = "hpd.thread-position.v1";
    internal static byte[] Encode(JournalPositionV1 value)
    {
        var writer = new CborWriter(CborConformanceMode.Ctap2Canonical);
        Write(writer, value);
        return writer.Encode();
    }

    internal static void Write(CborWriter writer, JournalPositionV1 value)
    {
        if (!value.IsValid)
            throw new ArgumentException("The journal position is invalid.", nameof(value));
        writer.WriteStartMap(2);
        writer.WriteUInt64(1);
        SessionAuthorityStampV1Codec.Write(writer, value.Session);
        writer.WriteUInt64(2);
        writer.WriteInt64(value.Sequence);
        writer.WriteEndMap();
    }

    internal static JournalPositionV1 ReadJournal(CborReader reader)
    {
        if (reader.ReadStartMap() != 2 || reader.ReadUInt64() != 1)
            throw new CborContentException("A journal position must contain exactly tags 1 and 2.");
        var session = SessionAuthorityStampV1Codec.Read(reader);
        if (reader.ReadUInt64() != 2)
            throw new CborContentException("A journal sequence must use tag 2.");
        var sequence = reader.ReadInt64();
        reader.ReadEndMap();
        return new JournalPositionV1(session, sequence);
    }

    internal static bool TryDecodeJournal(ReadOnlyMemory<byte> encoded, out JournalPositionV1 value)
    {
        value = default;
        try
        {
            var reader = new CborReader(encoded, CborConformanceMode.Ctap2Canonical, false);
            value = ReadJournal(reader);
            if (reader.BytesRemaining != 0)
                return false;
            return true;
        }
        catch (Exception exception) when (exception is CborContentException or InvalidOperationException or ArgumentException or OverflowException)
        {
            return false;
        }
    }

    internal static byte[] Encode(ThreadPositionV1 value)
    {
        if (!value.IsValid)
            throw new ArgumentException("The thread position is invalid.", nameof(value));
        Span<byte> thread = stackalloc byte[16];
        if (!value.ThreadId.TryWriteBytes(thread))
            throw new ArgumentException("The thread position is invalid.", nameof(value));
        var writer = new CborWriter(CborConformanceMode.Ctap2Canonical);
        writer.WriteStartMap(3);
        writer.WriteUInt64(1);
        writer.WriteByteString(thread);
        writer.WriteUInt64(2);
        writer.WriteInt64(value.Generation);
        writer.WriteUInt64(3);
        writer.WriteInt64(value.Sequence);
        writer.WriteEndMap();
        return writer.Encode();
    }

    internal static Hash256 ComputeHash(JournalPositionV1 value) => AuthorityIntegrityHashV1.Compute(JournalSchemaId, 1, 0, Encode(value));
    internal static Hash256 ComputeHash(ThreadPositionV1 value) => AuthorityIntegrityHashV1.Compute(ThreadSchemaId, 1, 0, Encode(value));

    internal static bool TryDecodeThread(ReadOnlyMemory<byte> encoded, out ThreadPositionV1 value)
    {
        value = default;
        try
        {
            var reader = new CborReader(encoded, CborConformanceMode.Ctap2Canonical, false);
            if (reader.ReadStartMap() != 3 || reader.ReadUInt64() != 1)
                return false;
            var thread = reader.ReadByteString();
            if (thread.Length != 16 || reader.ReadUInt64() != 2)
                return false;
            var generation = reader.ReadInt64();
            if (reader.ReadUInt64() != 3)
                return false;
            var sequence = reader.ReadInt64();
            reader.ReadEndMap();
            if (reader.BytesRemaining != 0 || generation <= 0 || sequence <= 0)
                return false;
            value = new(ThreadId.FromValue(StableId128.FromBytes(thread)), generation, sequence);
            return true;
        }
        catch (Exception exception) when (exception is CborContentException or InvalidOperationException or ArgumentException or OverflowException)
        {
            return false;
        }
    }
}
