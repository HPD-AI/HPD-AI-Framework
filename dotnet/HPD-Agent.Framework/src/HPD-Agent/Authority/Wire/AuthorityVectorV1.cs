using System.Collections.Immutable;
using System.Formats.Cbor;

namespace HPD.Agent.Authority;

/// <summary>Associates one registered sparse authority axis with its typed generation value.</summary>
public readonly record struct AxisEntryV1
{
    /// <summary>Initializes an axis entry from a closed typed generation value.</summary>
    /// <param name="value">The typed generation value.</param>
    /// <exception cref="ArgumentNullException">The value is missing.</exception>
    public AxisEntryV1(AuthorityAxisValueV1 value) => Value = value ?? throw new ArgumentNullException(nameof(value));

    /// <summary>Gets the registered axis identifier.</summary>
    public AuthorityAxisId AxisId => Value?.AxisId ?? 0;

    /// <summary>Gets the closed typed generation value.</summary>
    public AuthorityAxisValueV1 Value { get; }

    /// <summary>Gets whether the entry contains a registered sparse axis.</summary>
    public bool IsValid => Value is not null && AxisId is >= AuthorityAxisId.Graph and <= AuthorityAxisId.Transport;
}

/// <summary>Freezes the S1 session stamp and only the owner axes relevant to an operation.</summary>
public sealed class ExpectedAuthorityVectorV1 : IEquatable<ExpectedAuthorityVectorV1>
{
    private ExpectedAuthorityVectorV1(SessionAuthorityStampV1 session, ImmutableArray<AxisEntryV1> axes)
    {
        Session = session;
        Axes = axes;
    }

    /// <summary>Gets the required S1 session authority stamp.</summary>
    public SessionAuthorityStampV1 Session { get; }

    /// <summary>Gets the strictly sorted, duplicate-free sparse owner axes.</summary>
    public ImmutableArray<AxisEntryV1> Axes { get; }

    /// <summary>Creates a validated vector and canonicalizes its sparse axes by numeric axis ID.</summary>
    /// <param name="session">The required session authority stamp.</param>
    /// <param name="values">Zero or more relevant typed owner-axis values.</param>
    /// <returns>A validated immutable authority vector.</returns>
    /// <exception cref="ArgumentException">The session is invalid, a value is invalid, or an axis appears twice.</exception>
    /// <exception cref="ArgumentNullException">The values collection or one of its members is null.</exception>
    public static ExpectedAuthorityVectorV1 Create(SessionAuthorityStampV1 session, IEnumerable<AuthorityAxisValueV1> values)
    {
        if (!session.IsValid)
            throw new ArgumentException("A valid session authority stamp is required.", nameof(session));
        ArgumentNullException.ThrowIfNull(values);
        var entries = values.Select(value => new AxisEntryV1(value ?? throw new ArgumentNullException(nameof(values))))
            .OrderBy(entry => entry.AxisId)
            .ToImmutableArray();
        if (entries.Length > 256 || entries.Any(entry => !entry.IsValid) ||
            entries.Zip(entries.Skip(1), static (left, right) => left.AxisId == right.AxisId).Any(static duplicate => duplicate))
            throw new ArgumentException("Axes must be valid, duplicate-free, and bounded to 256 entries.", nameof(values));
        return new(session, entries);
    }

    /// <inheritdoc />
    public bool Equals(ExpectedAuthorityVectorV1? other) =>
        other is not null && Session == other.Session && Axes.AsSpan().SequenceEqual(other.Axes.AsSpan());

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is ExpectedAuthorityVectorV1 other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Session);
        foreach (var axis in Axes)
            hash.Add(axis);
        return hash.ToHashCode();
    }
}

internal static class AuthorityVectorCodecsV1
{
    internal static byte[] Encode(AxisEntryV1 value)
    {
        var writer = new CborWriter(CborConformanceMode.Ctap2Canonical);
        Write(writer, value);
        return writer.Encode();
    }

    internal static void Write(CborWriter writer, AxisEntryV1 value)
    {
        if (!value.IsValid)
            throw new ArgumentException("The axis entry is invalid.", nameof(value));
        Span<byte> bytes = stackalloc byte[16];
        if (!value.Value.TryWriteBytes(bytes))
            throw new ArgumentException("The axis generation is invalid.", nameof(value));
        writer.WriteStartMap(2);
        writer.WriteUInt64(1);
        writer.WriteUInt64((ushort)value.AxisId);
        writer.WriteUInt64(2);
        writer.WriteByteString(bytes);
        writer.WriteEndMap();
    }

    internal static AxisEntryV1 Read(CborReader reader)
    {
        if (reader.ReadStartMap() != 2 || reader.ReadUInt64() != 1)
            throw new CborContentException("An axis entry must contain exactly tags 1 and 2.");
        var axis = checked((ushort)reader.ReadUInt64());
        if (reader.ReadUInt64() != 2)
            throw new CborContentException("The axis value must use tag 2.");
        var bytes = reader.ReadByteString();
        if (bytes.Length != 16)
            throw new CborContentException("An axis generation is exactly 16 bytes.");
        reader.ReadEndMap();
        var stable = StableId128.FromBytes(bytes);
        AuthorityAxisValueV1 value = (AuthorityAxisId)axis switch
        {
            AuthorityAxisId.Graph => new AuthorityAxisValueV1.Graph(GraphGenerationId.FromValue(stable)),
            AuthorityAxisId.Activity => new AuthorityAxisValueV1.Activity(ActivityGenerationId.FromValue(stable)),
            AuthorityAxisId.Turn => new AuthorityAxisValueV1.Turn(TurnGenerationId.FromValue(stable)),
            AuthorityAxisId.Provider => new AuthorityAxisValueV1.Provider(ProviderGenerationId.FromValue(stable)),
            AuthorityAxisId.Output => new AuthorityAxisValueV1.Output(OutputGenerationId.FromValue(stable)),
            AuthorityAxisId.Sink => new AuthorityAxisValueV1.Sink(SinkGenerationId.FromValue(stable)),
            AuthorityAxisId.Tool => new AuthorityAxisValueV1.Tool(ToolGenerationId.FromValue(stable)),
            AuthorityAxisId.Route => new AuthorityAxisValueV1.Route(RouteGenerationId.FromValue(stable)),
            AuthorityAxisId.Privacy => new AuthorityAxisValueV1.Privacy(PrivacyGenerationId.FromValue(stable)),
            AuthorityAxisId.Transport => new AuthorityAxisValueV1.Transport(TransportGenerationId.FromValue(stable)),
            _ => throw new CborContentException("The axis is not registered for sparse vectors."),
        };
        return new(value);
    }

    internal static byte[] Encode(ExpectedAuthorityVectorV1 value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var writer = new CborWriter(CborConformanceMode.Ctap2Canonical);
        writer.WriteStartMap(2);
        writer.WriteUInt64(1);
        SessionAuthorityStampV1Codec.Write(writer, value.Session);
        writer.WriteUInt64(2);
        writer.WriteStartArray(value.Axes.Length);
        foreach (var entry in value.Axes)
            Write(writer, entry);
        writer.WriteEndArray();
        writer.WriteEndMap();
        return writer.Encode();
    }

    internal static bool TryDecodeVector(ReadOnlyMemory<byte> encoded, out ExpectedAuthorityVectorV1? value)
    {
        value = null;
        try
        {
            var reader = new CborReader(encoded, CborConformanceMode.Ctap2Canonical, false);
            if (reader.ReadStartMap() != 2 || reader.ReadUInt64() != 1)
                return false;
            var session = SessionAuthorityStampV1Codec.Read(reader);
            if (reader.ReadUInt64() != 2)
                return false;
            var count = reader.ReadStartArray();
            if (count is null or > 256)
                return false;
            var axes = new List<AuthorityAxisValueV1>(count.Value);
            AuthorityAxisId previous = 0;
            for (var index = 0; index < count; index++)
            {
                var entry = Read(reader);
                if (entry.AxisId <= previous)
                    return false;
                previous = entry.AxisId;
                axes.Add(entry.Value);
            }
            reader.ReadEndArray();
            reader.ReadEndMap();
            if (reader.BytesRemaining != 0)
                return false;
            value = ExpectedAuthorityVectorV1.Create(session, axes);
            return true;
        }
        catch (Exception exception) when (exception is CborContentException or InvalidOperationException or ArgumentException or OverflowException)
        {
            return false;
        }
    }
}
